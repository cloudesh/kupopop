using System.Security.Cryptography;
using Kupopop.Shared;

namespace Kupopop.Server;

public sealed partial class GameEngine(AccountStore accounts, ILogger<GameEngine> log, TimeProvider clock, WordBank? wordBank = null,
    Func<int>? customWordRoll = null)
{
    private sealed class Member(Guid id)
    {
        public Guid Id { get; } = id;
        public int Score;
        public bool Solved;
        public bool Connected = true;
        public DateTimeOffset DisconnectedAt;
    }

    private sealed class Room(string code, Guid host, RoomOptions options)
    {
        public long MonitorId;
        public HashSet<Guid> StatsParticipants = [];
        public HashSet<Guid> DrawingLikes = [];
        public string[] HostWords = [];
        public bool StatsRecorded;
        public string Code = code;
        public Guid Host = host;
        public RoomOptions Options = options;
        public Dictionary<Guid, Member> Members = [];
        public PixelCanvas Canvas = new();
        public CanvasHistory History = new();
        public KickBallot? Vote;
        public bool VoteUsed;
        public KickBallot? SkipVote;
        public bool SkipVoteUsed;
        public string[] Choices = [];
        public bool CustomWordAvailable;
        public string[] Answers = [];
        public Dictionary<Guid, SolveResult> MatchScores = [];
        public SolveResult[] Results = [];
        public int[] HintOrder = [];
        public List<SolveResult> Solvers = [];
        public BrushCursor? Cursor;
        public DateTimeOffset CursorAt;
        public string Phase = "waiting";
        public List<Guid> Deck = [];
        public int Position = -1;
        public int RotationSize;
        public Guid? Drawer;
        public Guid TurnId;
        public string Word = "";
        public string LastWord = "";
        public DateTimeOffset Deadline;
        public int Revealed;
        public DateTimeOffset EmptySince;
    }

    private readonly object gate = new();
    private readonly Dictionary<Guid, IGamePeer> peers = [];
    private readonly Dictionary<string, Room> rooms = new(StringComparer.OrdinalIgnoreCase);
    private long nextMonitorId;
    private readonly WordBank words = wordBank ?? WordBank.Starter();
    private sealed class KickBallot(Guid target, Guid[] eligible, int required, DateTimeOffset expires)
    {
        public Guid Target = target;
        public HashSet<Guid> Eligible = eligible.ToHashSet();
        public HashSet<Guid> Votes = [];
        public int Required = required;
        public DateTimeOffset Expires = expires;
    }
    private DateTimeOffset Now => clock.GetUtcNow();

    public ServerStatus GetStatus(TimeSpan uptime, bool running = true)
    {
        lock (gate)
            return new(Now, running, (long)uptime.TotalSeconds, peers.Count,
                peers.Values.Count(p => p.RoomCode is null), rooms.Values.Select(r => new RoomStatus(
                    r.MonitorId, r.Options.IsPublic, r.Phase, r.Members.Values.Count(m => m.Connected),
                    r.Members.Count, r.Options.MaxPlayers, r.RotationSize > 0 ? Math.Min(r.Options.Rounds, r.Position / r.RotationSize + 1) : 0,
                    r.Options.Rounds)).ToArray());
    }

    public bool TryAttach(IGamePeer peer, string accessToken)
    {
        lock (gate)
        {
            // Recheck under the same lock used by Revoke: recovery cannot race
            // an already-authenticated WebSocket handshake into a new session.
            if (accounts.Authenticate(accessToken)?.Id != peer.Id) return false;
            Attach(peer);
            return true;
        }
    }

    public void Attach(IGamePeer peer)
    {
        lock (gate)
        {
            if (peers.Remove(peer.Id, out var old)) old.Close();
            peers[peer.Id] = peer;
            peer.Send(new("profile", Profile: accounts.Get(peer.Id)));
            peer.Send(new("wordlists", WordLists: words.Catalog));
            var room = rooms.Values.FirstOrDefault(r => r.Members.ContainsKey(peer.Id));
            if (room is not null)
            {
                peer.RoomCode = room.Code;
                room.Members[peer.Id].Connected = true;
                State(room, peer, snapshot: true);
                BroadcastState(room, except: peer.Id);
            }
            log.LogInformation("Connection attached. Online={Online} Rooms={Rooms}", peers.Count, rooms.Count);
            SendSocial(peer);
            RefreshFriends(peer.Id, presenceOnly: true);
        }
    }

    public void Detach(IGamePeer peer, bool intentional = false)
    {
        lock (gate)
        {
            if (!peers.TryGetValue(peer.Id, out var active) || !ReferenceEquals(active, peer)) return;
            peers.Remove(peer.Id);
            if (!accounts.Preferences(peer.Id).BackgroundInvitations) invitations.Remove(peer.Id);
            nextInvitation.Remove(peer.Id);
            if (intentional) recentPlayers.Remove(peer.Id);
            RefreshFriends(peer.Id, presenceOnly: true);
            if (intentional && FindRoom(peer) is { } leaving)
            {
                Remove(leaving, peer.Id);
                peer.RoomCode = null;
                log.LogInformation("Window closed. Online={Online} Rooms={Rooms}", peers.Count, rooms.Count);
                return;
            }
            if (FindRoom(peer) is { } room && room.Members.TryGetValue(peer.Id, out var member))
            {
                member.Connected = false;
                member.DisconnectedAt = Now;
                TransferHost(room);
                BroadcastState(room);
            }
            log.LogInformation("Connection detached. Online={Online}", peers.Count);
        }
    }

    public void Revoke(Guid id)
    {
        lock (gate) if (peers.TryGetValue(id, out var peer)) { peer.Close(); Detach(peer); }
    }

    public void Handle(IGamePeer peer, ClientCommand command)
    {
        lock (gate)
        {
            if (!peers.TryGetValue(peer.Id, out var active) || !ReferenceEquals(active, peer)) return;
            if (command.Type is not { Length: > 0 and <= 40 } || command.Type.Any(c => !char.IsAsciiLetterLower(c) && c != '-'))
            { Error(peer, "Command rejected."); return; }
            string rateKind = command.Type == "draw" && command.Draw is { } drawingOp
                ? drawingOp.Tool == "fill" ? "fill" : DrawingTools.Shape(drawingOp.Tool) ? "shape" : "draw" : command.Type;
            if (command.Type.StartsWith("leaderboard", StringComparison.Ordinal)) rateKind = "leaderboard";
            if (!peer.Allow(rateKind)) { Error(peer, "Slow down; message limit reached.", command.Type); return; }
            var room = FindRoom(peer);
            try
            {
                switch (command.Type)
                {
                    case "social": SendSocial(peer); break;
                    case "suggest":
                        string? category = command.Kind == "word" ? command.Category?.Trim() : null;
                        if (command.Kind == "word" && !Protocol.ValidName(category))
                            throw new ArgumentException("Choose or enter a category of 2–40 characters.");
                        accounts.SubmitSuggestion(peer.Id, command.Kind, category, command.Text, Now);
                        peer.Send(new("suggestion-sent"));
                        break;
                    case "leaderboard":
                        peer.Send(new("leaderboard", Leaderboard: accounts.Leaderboard(peer.Id, command.Text ?? "best", command.Page)));
                        break;
                    case "leaderboard-name":
                    case "leaderboard-hidden":
                    case "leaderboard-portrait":
                        accounts.SetLeaderboardPreference(peer.Id, command.Type[12..], command.Enabled);
                        peer.Send(new("leaderboard", Leaderboard: accounts.Leaderboard(peer.Id, "best", 0)));
                        break;
                    case "like-drawing": LikeDrawing(peer, room, command.TurnId); break;
                    case "friend-request": ChangeFriend(peer, command.TargetId, "request"); break;
                    case "friend-accept": ChangeFriend(peer, command.TargetId, "accept"); break;
                    case "friend-remove": ChangeFriend(peer, command.TargetId, "remove"); break;
                    case "view-profile": ViewProfile(peer, command.TargetId); break;
                    case "profile-privacy":
                        accounts.SetPreferences(peer.Id, command.Enabled, command.Shared, command.ResetAvatar, command.BackgroundInvitations);
                        if (command.BackgroundInvitations == false) invitations.Remove(peer.Id);
                        RefreshFriends(peer.Id);
                        foreach (var activeRoom in rooms.Values) activeRoom.StatsParticipants.Remove(peer.Id);
                        SendSocial(peer); ViewProfile(peer, peer.Id);
                        break;
                    case "invite": Invite(peer, room, command.TargetId); break;
                    case "accept-invite": AcceptInvitation(peer, room, command.TargetId); break;
                    case "dismiss-invite":
                        if (invitations.TryGetValue(peer.Id, out var inbox)) inbox.RemoveAll(i => i.View.Id == command.TargetId);
                        SendSocial(peer); break;
                    case "list":
                        peer.Send(new("rooms", Rooms: rooms.Values.Where(r => r.Options.IsPublic)
                            .Select(r => new RoomSummary(r.Code, r.Options.Title,
                                r.Members.Values.Count(m => m.Connected), r.Options.MaxPlayers, r.Phase)).ToArray()));
                        break;
                    case "create":
                        if (room is not null) throw new ArgumentException("Leave your current room first.");
                        if (rooms.Count >= 100) throw new ArgumentException("Server room capacity reached.");
                        var hostWords = CustomWordLists.Parse(command.CustomWords);
                        var options = Validate(command.Options ?? new("A kupo drawing room"), hostWords);
                        string code;
                        do code = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)); while (rooms.ContainsKey(code));
                        room = new(code, peer.Id, options);
                        room.HostWords = hostWords;
                        room.MonitorId = ++nextMonitorId;
                        room.Members[peer.Id] = new(peer.Id);
                        rooms[code] = room;
                        peer.RoomCode = code;
                        State(room, peer, snapshot: true);
                        log.LogInformation("Room created. Public={Public} Capacity={Capacity}", options.IsPublic, options.MaxPlayers);
                        break;
                    case "join":
                        if (room is not null) throw new ArgumentException("Leave your current room first.");
                        string requested = command.Text?.Trim() ?? "";
                        if (!rooms.TryGetValue(requested, out room)) throw new ArgumentException("Room not found.");
                        if (room.Phase is not ("waiting" or "results")) throw new ArgumentException("This match has started. Join after it finishes.");
                        if (room.Members.Count >= room.Options.MaxPlayers) throw new ArgumentException("Room is full.");
                        room.Members[peer.Id] = new(peer.Id);
                        peer.RoomCode = room.Code;
                        RememberRoom(room);
                        State(room, peer, snapshot: true);
                        SystemMessage(room, "A player joined the room.");
                        BroadcastState(room, except: peer.Id);
                        break;
                    case "leave":
                        if (room is not null) Remove(room, peer.Id);
                        peer.RoomCode = null;
                        peer.Send(new("left"));
                        break;
                    case "start":
                        RequireHost(peer, room);
                        if (room!.Phase is not ("waiting" or "results")) throw new ArgumentException("A match is already running.");
                        var participants = room.Members.Values.Where(m => m.Connected).Select(m => m.Id).ToArray();
                        if (participants.Length < 2) throw new ArgumentException("At least two connected players are needed.");
                        foreach (var member in room.Members.Values) { member.Score = 0; member.Solved = false; }
                        room.MatchScores = participants.ToDictionary(id => id, id => new SolveResult(id, accounts.Get(id)!.DisplayName, 0));
                        room.StatsParticipants = participants.Where(id => accounts.Preferences(id).TrackStats).ToHashSet();
                        room.StatsRecorded = false;
                        room.Results = [];
                        room.Deck = [];
                        for (int i = 0; i < room.Options.Rounds; i++) room.Deck.AddRange(participants);
                        room.RotationSize = participants.Length;
                        room.Position = -1;
                        NextTurn(room);
                        break;
                    case "draw":
                        if (room?.Phase != "drawing" || room.Drawer != peer.Id || command.TurnId != room.TurnId)
                            throw new ArgumentException("Drawing is only available during your current turn.");
                        if (Now >= room.Deadline) { EndTurn(room); break; }
                        if (command.Draw is null || !room.Canvas.Valid(command.Draw)) throw new ArgumentException("Drawing operation rejected.");
                        room.History.Capture(room.Canvas, command.Draw.StrokeId);
                        room.Canvas.Apply(command.Draw);
                        Broadcast(room, new("draw", Draw: command.Draw, ActorId: peer.Id, TurnId: room.TurnId));
                        break;
                    case "choose-word":
                        if (room?.Phase != "choosing" || room.Drawer != peer.Id || command.TurnId != room.TurnId ||
                            Now >= room.Deadline || command.Text is null || !room.Choices.Contains(command.Text, StringComparer.Ordinal))
                            throw new ArgumentException("Choose one of your offered words before the timer ends.");
                        BeginDrawing(room, command.Text);
                        break;
                    case "choose-custom-word":
                        if (room?.Phase != "choosing" || room.Drawer != peer.Id || command.TurnId != room.TurnId ||
                            Now >= room.Deadline || !room.Options.CustomWordsEnabled || !room.CustomWordAvailable)
                            throw new ArgumentException("A custom word is not available for this turn.");
                        string custom = command.Text?.Trim() ?? "";
                        if (!GuessMatching.ValidCustomWord(custom))
                            throw new ArgumentException("Use 2–48 letters, numbers, spaces, apostrophes or hyphens, with at least two letters.");
                        BeginDrawing(room, custom, custom: true);
                        break;
                    case "configure":
                        RequireHost(peer, room);
                        if (room!.Phase is not ("waiting" or "results")) throw new ArgumentException("Change room settings between matches.");
                        var updatedWords = command.CustomWords is null ? room.HostWords : CustomWordLists.Parse(command.CustomWords);
                        var updated = Validate(command.Options ?? room.Options, updatedWords);
                        if (updated.MaxPlayers < room.Members.Count) throw new ArgumentException("Capacity cannot be smaller than the current room.");
                        room.Options = updated;
                        room.HostWords = updatedWords;
                        BroadcastState(room);
                        peer.Send(new("room-configured"));
                        break;
                    case "cursor":
                        if (room?.Phase != "drawing" || room.Drawer != peer.Id || command.TurnId != room.TurnId || Now >= room.Deadline)
                            break; // A cursor already in flight may arrive after the turn changes.
                        var cursor = command.Cursor;
                        if (cursor is null || cursor.X is < 0 or >= Protocol.CanvasWidth || cursor.Y is < 0 or >= Protocol.CanvasHeight ||
                            !DrawingTools.Valid(cursor.Tool) || !Palette.Valid(cursor.Color, false) || cursor.Size is < 1 or > 20)
                            throw new ArgumentException("Canvas cursor rejected.");
                        room.Cursor = cursor; room.CursorAt = Now;
                        Broadcast(room, new("cursor", Cursor: cursor, ActorId: peer.Id, TurnId: room.TurnId));
                        break;
                    case "undo":
                        if (room?.Phase != "drawing" || room.Drawer != peer.Id || command.TurnId != room.TurnId || Now >= room.Deadline)
                            throw new ArgumentException("Undo is only available during your current turn.");
                        if (!room.History.Undo(room.Canvas)) throw new ArgumentException("Nothing to undo.");
                        Broadcast(room, new("canvas", Canvas: (byte[])room.Canvas.Pixels.Clone(), TurnId: room.TurnId, CanUndo: room.History.CanUndo));
                        break;
                    case "vote-kick":
                        VoteKick(peer, room, command.TurnId, skip: false);
                        break;
                    case "vote-skip":
                        VoteKick(peer, room, command.TurnId, skip: true);
                        break;
                    case "chat":
                        Chat(peer, room, command.Text ?? "");
                        break;
                    case "profile":
                        var profile = accounts.Update(peer.Id, command.Text, command.Avatar, command.ResetAvatar, command.AvatarPreset);
                        peer.Send(new("profile", Profile: profile));
                        if (room is not null) BroadcastState(room);
                        RefreshFriends(peer.Id);
                        break;
                    case "kick":
                        RequireHost(peer, room);
                        if (!Guid.TryParse(command.Text, out var target) || target == peer.Id || !room!.Members.ContainsKey(target))
                            throw new ArgumentException("Select another player to remove.");
                        Remove(room, target);
                        if (peers.TryGetValue(target, out var kicked))
                        { kicked.RoomCode = null; kicked.Send(new("left", Error: "The host removed you from the room.")); }
                        break;
                    case "sync":
                        if (room is not null) State(room, peer, snapshot: true);
                        break;
                    case "ping": peer.Send(new("pong")); break;
                    default: Error(peer, "Unknown command; check the plugin and server versions."); break;
                }
            }
            catch (ArgumentException ex) { Error(peer, ex.Message, command.Type); }
        }
    }

    public void Tick()
    {
        lock (gate)
        {
            CleanInvitationInboxes();
            foreach (var room in rooms.Values.ToArray())
            {
                foreach (var member in room.Members.Values.Where(m => !m.Connected && Now - m.DisconnectedAt > TimeSpan.FromSeconds(30)).ToArray())
                {
                    Remove(room, member.Id);
                    recentPlayers.Remove(member.Id);
                }
                if (!rooms.ContainsKey(room.Code)) continue;
                if (room.Members.Values.All(m => !m.Connected))
                {
                    if (room.EmptySince == default) room.EmptySince = Now;
                    if (Now - room.EmptySince > TimeSpan.FromSeconds(60)) rooms.Remove(room.Code);
                    continue;
                }
                room.EmptySince = default;
                if (room.Vote is { } ballot && Now >= ballot.Expires) { room.Vote = null; BroadcastState(room); }
                if (room.SkipVote is { } skipBallot && Now >= skipBallot.Expires) { room.SkipVote = null; BroadcastState(room); }
                if (room.Phase == "choosing")
                {
                    if (room.Drawer is null || !room.Members.TryGetValue(room.Drawer.Value, out var choosing) || !choosing.Connected)
                        EndTurn(room);
                    else if (Now >= room.Deadline) BeginDrawing(room, room.Choices[RandomNumberGenerator.GetInt32(room.Choices.Length)]);
                }
                if (room.Phase == "drawing")
                {
                    if (Now >= room.Deadline || room.Drawer is null ||
                        !room.Members.TryGetValue(room.Drawer.Value, out var drawer) || !drawer.Connected)
                    { EndTurn(room); continue; }
                    int elapsed = (int)Math.Max(0, room.Options.TurnSeconds - (room.Deadline - Now).TotalSeconds);
                    int reveal = room.Options.HintSeconds <= 0 ? 0 : elapsed / room.Options.HintSeconds;
                    reveal = Math.Min(reveal, room.Word.Count(char.IsLetterOrDigit) / 2);
                    if (reveal != room.Revealed) { room.Revealed = reveal; BroadcastState(room); }
                }
                else if (room.Phase == "intermission" && Now >= room.Deadline) NextTurn(room);
            }
        }
    }

    private void Chat(IGamePeer peer, Room? room, string text)
    {
        if (room is null) throw new ArgumentException("Join a room to chat.");
        text = text.Trim();
        if (text.Length is < 1 or > 300 || text.Any(char.IsControl)) throw new ArgumentException("Messages must be 1–300 characters.");
        var member = room.Members[peer.Id];
        bool done = room.Phase == "drawing" && (room.Drawer == peer.Id || member.Solved);
        if (room.Phase == "drawing" && !done)
        {
            if (Now >= room.Deadline) { EndTurn(room); return; }
            string guess = GuessMatching.Normalize(text);
            if (room.Answers.Contains(guess, StringComparer.Ordinal))
            {
                int rank = room.Solvers.Count + 1;
                member.Solved = true;
                int award = Math.Max(100, 1000 - (rank - 1) * 60);
                member.Score += award;
                room.Solvers.Add(new(peer.Id, accounts.Get(peer.Id)!.DisplayName, award));
                if (room.Drawer is { } drawerId && room.Members.TryGetValue(drawerId, out var drawer)) drawer.Score += 100;
                foreach (var participant in room.Members.Values)
                    room.MatchScores[participant.Id] = new(participant.Id, accounts.Get(participant.Id)!.DisplayName, participant.Score);
                SystemMessage(room, $"{accounts.Get(peer.Id)!.DisplayName} guessed the word! (+{award})");
                log.LogInformation("Correct guess accepted. Rank={Rank}", rank);
                if (room.Members.Values.Where(m => m.Connected && m.Id != room.Drawer).All(m => m.Solved)) EndTurn(room);
                else BroadcastState(room);
                return;
            }
            if (room.Answers.Any(answer => GuessMatching.IsNear(guess, answer)))
            {
                peer.Send(new("chat", Chat: new(null, "Kupo Skribbl", "So close! Try again.", System: true)));
                return;
            }
        }
        var line = new ChatLine(peer.Id, accounts.Get(peer.Id)!.DisplayName, text, Done: done);
        foreach (var recipient in room.Members.Values.Where(m => m.Connected))
            if (!done || recipient.Solved || recipient.Id == room.Drawer)
                if (peers.TryGetValue(recipient.Id, out var receiver)) receiver.Send(new("chat", Chat: line));
        // Message text and guesses are deliberately never written to logs.
    }

    private void NextTurn(Room room)
    {
        room.Vote = room.SkipVote = null; room.VoteUsed = room.SkipVoteUsed = false; room.History.Clear(); room.Cursor = null;
        do room.Position++; while (room.Position < room.Deck.Count &&
            (!room.Members.TryGetValue(room.Deck[room.Position], out var member) || !member.Connected));
        if (room.Position >= room.Deck.Count || room.Members.Values.Count(m => m.Connected) < 2)
        {
            room.Phase = "results";
            room.Drawer = null;
            room.Deadline = default;
            room.CustomWordAvailable = false;
            room.Results = room.MatchScores.Values.OrderByDescending(s => s.Points).ThenBy(s => s.Name, StringComparer.Ordinal).ToArray();
            if (!room.StatsRecorded && room.Position >= room.Deck.Count && room.Results.Length >= 2)
            {
                if (room.HostWords.Length == 0) accounts.RecordMatch(room.Results, room.StatsParticipants);
                room.StatsRecorded = true;
            }
            SystemMessage(room, "Match finished! The host can start another match.");
            BroadcastState(room);
            log.LogInformation("Match finished. Players={Players}", room.Members.Count);
            return;
        }
        room.Phase = "choosing";
        room.Drawer = room.Deck[room.Position];
        room.TurnId = Guid.NewGuid();
        room.DrawingLikes.Clear();
        room.Deadline = Now.AddSeconds(15);
        room.Revealed = 0;
        room.Word = "";
        room.Choices = words.Choices(room.Options.WordLists, room.LastWord, room.Options.WordChoiceCount, room.HostWords);
        room.CustomWordAvailable = room.Options.CustomWordsEnabled && (customWordRoll?.Invoke() ?? RandomNumberGenerator.GetInt32(100)) < 10;
        room.Answers = [];
        room.Solvers.Clear(); room.HintOrder = [];
        room.Canvas.Clear();
        foreach (var member in room.Members.Values) member.Solved = false;
        BroadcastState(room, reset: true);
        SystemMessage(room, $"{accounts.Get(room.Drawer.Value)!.DisplayName} is choosing the next word.");
        log.LogInformation("Word selection started. Round={Round} Players={Players}", room.Position / room.RotationSize + 1, room.Members.Count);
    }

    private void BeginDrawing(Room room, string word, bool custom = false)
    {
        room.Word = room.LastWord = word; room.Choices = [];
        room.CustomWordAvailable = false;
        room.Answers = custom ? [GuessMatching.Normalize(word)] : words.Answers(word, room.Options.WordLists);
        room.HintOrder = Enumerable.Range(0, word.Length).Where(i => char.IsLetterOrDigit(word[i])).ToArray();
        RandomNumberGenerator.Shuffle(room.HintOrder.AsSpan());
        room.Phase = "drawing"; room.Deadline = Now.AddSeconds(room.Options.TurnSeconds); room.Revealed = 0;
        BroadcastState(room);
        log.LogInformation("Drawing turn started. Players={Players}", room.Members.Count);
    }

    private void EndTurn(Room room)
    {
        if (room.Phase is not ("drawing" or "choosing")) return;
        room.Vote = room.SkipVote = null; room.History.Clear(); room.Cursor = null; room.Choices = [];
        room.CustomWordAvailable = false;
        room.Phase = "intermission";
        room.Deadline = Now.AddSeconds(6);
        BroadcastState(room);
    }

    private void Remove(Room room, Guid id)
    {
        room.Members.Remove(id);
        if (peers.TryGetValue(id, out var peer)) peer.RoomCode = null;
        if (room.Members.Count == 0) { rooms.Remove(room.Code); return; }
        TransferHost(room);
        if (room.Phase is "drawing" or "choosing" && room.Drawer == id) EndTurn(room);
        BroadcastState(room);
    }

    private static void TransferHost(Room room)
    {
        if (!room.Members.TryGetValue(room.Host, out var host) || !host.Connected)
            room.Host = room.Members.Values.FirstOrDefault(m => m.Connected)?.Id ?? room.Members.Keys.First();
        if (room.Vote?.Target == room.Host) room.Vote = null;
    }

    private Room? FindRoom(IGamePeer peer) => peer.RoomCode is { } code ? rooms.GetValueOrDefault(code) : null;
    private static void Error(IGamePeer peer, string message, string? command = null) => peer.Send(new("error", Error: message, RejectedCommand: command));
    private static void RequireHost(IGamePeer peer, Room? room)
    {
        if (room is null || room.Host != peer.Id) throw new ArgumentException("Only the room host can do that.");
    }

    private PlayerView[] PlayerViews(Room room) => room.Members.Values.Select(member => new PlayerView(
        accounts.Get(member.Id)!, member.Score, member.Connected, member.Solved,
        member.Id == room.Host, member.Id == room.Drawer)).ToArray();

    private void State(Room room, IGamePeer peer, bool snapshot = false, bool reset = false, PlayerView[]? players = null)
    {
        var member = room.Members.GetValueOrDefault(peer.Id);
        bool done = room.Phase == "drawing" && (room.Drawer == peer.Id || member?.Solved == true);
        bool showWord = done || room.Phase is "intermission" or "results";
        var letters = room.HintOrder.Take(room.Revealed).ToHashSet();
        string hint = string.Concat(room.Word.Select((c, i) => !char.IsLetterOrDigit(c) || letters.Contains(i) ? c : '_'));
        peer.Send(new("state", Room: new(room.Code, room.Options, room.Phase, room.Host, room.Drawer,
            room.TurnId, room.RotationSize == 0 ? 0 : Math.Min(room.Options.Rounds, room.Position / room.RotationSize + 1),
            room.Deadline, hint, showWord ? room.Word : null, done,
            players ?? PlayerViews(room), Now,
            snapshot ? (byte[])room.Canvas.Pixels.Clone() : null, reset,
            room.Phase == "drawing" && room.Drawer == peer.Id && room.History.CanUndo,
            room.Vote is { } vote ? new(vote.Target, vote.Votes.Count, vote.Required, vote.Votes.Contains(peer.Id), vote.Expires) : null,
            room.Phase == "choosing" && room.Drawer == peer.Id ? (string[])room.Choices.Clone() : null,
            room.Phase is "intermission" or "results" ? room.Solvers.Take(3).ToArray() : null,
            room.SkipVote is { } skip ? new(skip.Target, skip.Votes.Count, skip.Required, skip.Votes.Contains(peer.Id), skip.Expires) : null,
            room.Phase == "drawing" && Now - room.CursorAt < TimeSpan.FromSeconds(2) ? room.Cursor : null,
            room.Options.VoteKickEnabled && room.Drawer != room.Host && (!room.VoteUsed || room.Vote is not null),
            room.Options.VoteSkipEnabled && (!room.SkipVoteUsed || room.SkipVote is not null),
            room.Phase == "choosing" && room.Drawer == peer.Id && room.CustomWordAvailable,
            room.Phase == "results" ? room.Results : null,
            room.DrawingLikes.Count, room.DrawingLikes.Contains(peer.Id)),
            HostWords: room.Host == peer.Id && room.Phase is "waiting" or "results" ? (string[])room.HostWords.Clone() : null));
    }

    private void BroadcastState(Room room, bool reset = false, Guid? except = null)
    {
        var players = PlayerViews(room);
        foreach (var member in room.Members.Values)
            if (member.Id != except && member.Connected && peers.TryGetValue(member.Id, out var peer)) State(room, peer, reset: reset, players: players);
    }

    private void Broadcast(Room room, ServerEvent message)
    {
        foreach (var member in room.Members.Values)
            if (member.Connected && peers.TryGetValue(member.Id, out var peer)) peer.Send(message);
    }

    private void SystemMessage(Room room, string text) => Broadcast(room, new("chat", Chat: new(null, "Kupopop", text, System: true)));

    private RoomOptions Validate(RoomOptions options, string[]? custom = null)
    {
        string title = options.Title?.Trim() ?? "";
        if (!Protocol.ValidName(title)) throw new ArgumentException("Room title must be 2–40 characters.");
        if (options.TurnSeconds is < 15 or > 300 || options.Rounds is < 1 or > 5 ||
            options.MaxPlayers is < 2 or > 16 || options.HintSeconds is < 0 or > 300 || options.WordChoiceCount is < 1 or > 8)
            throw new ArgumentException("Room settings are outside supported limits.");
        string[] selection = words.Selection(options.WordLists, allowEmpty: custom is { Length: > 0 });
        words.Choices(selection, "", options.WordChoiceCount, custom);
        return options with { Title = title, WordLists = selection, CustomWordCount = custom?.Length ?? 0 };
    }

    private void VoteKick(IGamePeer peer, Room? room, Guid turn, bool skip)
    {
        if (room is not null && !(skip ? room.Options.VoteSkipEnabled : room.Options.VoteKickEnabled))
            throw new ArgumentException("This room has disabled that vote option.");
        if (room?.Phase != "drawing" || turn != room.TurnId || Now >= room.Deadline ||
            room.Drawer is not { } target || target == peer.Id || !room.Members[peer.Id].Connected)
            throw new ArgumentException("You may only vote on the current drawer.");
        var activeVote = skip ? room.SkipVote : room.Vote;
        if (!skip && target == room.Host) throw new ArgumentException("The room leader cannot be vote-kicked.");
        if (activeVote is null)
        {
            if (skip ? room.SkipVoteUsed : room.VoteUsed) throw new ArgumentException("A vote has already ended this turn.");
            var online = room.Members.Values.Where(m => m.Connected).ToArray();
            Guid[] eligible = online.Where(m => m.Id != target).Select(m => m.Id).ToArray();
            int required = online.Length < 5 ? eligible.Length : (int)Math.Ceiling(eligible.Length * 0.7);
            activeVote = new(target, eligible, required, Now.AddSeconds(30));
            if (skip) { room.SkipVote = activeVote; room.SkipVoteUsed = true; }
            else { room.Vote = activeVote; room.VoteUsed = true; }
        }
        var ballot = activeVote;
        if (Now >= ballot.Expires || !ballot.Eligible.Contains(peer.Id) || !ballot.Votes.Add(peer.Id))
            throw new ArgumentException("This vote has ended or your vote was already counted.");
        int remaining = Math.Max(0, ballot.Required - ballot.Votes.Count);
        SystemMessage(room, $"Vote to {(skip ? "skip" : "kick")}: {ballot.Votes.Count}/{ballot.Required}. {remaining} more {(remaining == 1 ? "vote" : "votes")} needed.");
        if (ballot.Votes.Count >= ballot.Required)
        {
            if (skip)
            {
                EndTurn(room); SystemMessage(room, "The room voted to skip this turn.");
                return;
            }
            room.Vote = null;
            Remove(room, target);
            if (peers.TryGetValue(target, out var removed)) removed.Send(new("left", Error: "The room voted to remove you."));
            SystemMessage(room, "The drawer was removed by vote.");
            log.LogInformation("Drawer removed by room vote. Votes={Votes} Required={Required}", ballot.Votes.Count, ballot.Required);
        }
        else BroadcastState(room);
    }

}
