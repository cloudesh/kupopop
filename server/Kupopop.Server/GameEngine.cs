using System.Security.Cryptography;
using Kupopop.Shared;

namespace Kupopop.Server;

public sealed partial class GameEngine(AccountStore accounts, ILogger<GameEngine> log, TimeProvider clock, WordBank? wordBank = null,
    Func<int>? customWordRoll = null, Func<int, int>? effectRoll = null)
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
        public HashSet<Guid> WordViewers = [];
        public HashSet<Guid> BannedAccounts = [];
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
        public RoomEffect Effect;
        public byte[]? Crayons;
        public DateTimeOffset? EncoreUntil;
        public HashSet<Guid> PomSenders = [];
        public Dictionary<Guid, int> BonusPoints = [];
        public bool CustomWordAvailable;
        public string[] Answers = [];
        public Dictionary<Guid, SolveResult> MatchScores = [];
        public SolveResult[] Results = [];
        public int[] HintOrder = [];
        public List<SolveResult> Solvers = [];
        public BrushCursor? Cursor;
        public DateTimeOffset CursorAt;
        public string Phase = "waiting";
        public List<Guid> TurnOrder = [];
        public int TurnIndex = -1;
        public int Round;
        public Guid? Drawer;
        public Guid TurnId;
        public string Word = "";
        public string LastWord = "";
        public DateTimeOffset Deadline;
        public int Revealed;
        public DateTimeOffset EmptySince;
    }

    // Lock order is engine -> account store. Store methods must never call back
    // into the engine while holding their lock. Network sends only enqueue here.
    private readonly object gate = new();
    private readonly Dictionary<Guid, IGamePeer> peers = [];
    private readonly Dictionary<string, Room> rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, DateTimeOffset> disconnectedSessions = [];
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
        {
            return new(Now, running, (long)uptime.TotalSeconds, peers.Count,
                peers.Values.Count(p => p.RoomCode is null), rooms.Values.Select(r => new RoomStatus(
                    r.MonitorId, r.Options.IsPublic, r.Phase, r.Members.Values.Count(m => m.Connected),
                    r.Members.Count, r.Options.MaxPlayers, r.Round,
                    r.Options.Rounds)).ToArray());
        }
    }

    public bool TryAttach(IGamePeer peer, string accessToken)
    {
        lock (gate)
        {
            // Recheck under the same lock used by Revoke: recovery cannot race
            // an already-authenticated WebSocket handshake into a new session.
            if (accounts.Authenticate(accessToken)?.Id != peer.Id)
            {
                return false;
            }

            Attach(peer);
            return true;
        }
    }

    public void Attach(IGamePeer peer)
    {
        lock (gate)
        {
            ExpireDisconnectedSessions();
            disconnectedSessions.Remove(peer.Id);
            if (peers.Remove(peer.Id, out var old))
            {
                old.Close();
            }

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
            log.LogDebug("Connection attached. Online={Online} Rooms={Rooms}", peers.Count, rooms.Count);
            SendSocial(peer);
            RefreshFriends(peer.Id, presenceOnly: true);
        }
    }

    public void Detach(IGamePeer peer, bool intentional = false)
    {
        lock (gate)
        {
            if (!peers.TryGetValue(peer.Id, out var active) || !ReferenceEquals(active, peer))
            {
                return;
            }

            peers.Remove(peer.Id);
            if (!accounts.Preferences(peer.Id).BackgroundInvitations)
            {
                invitations.Remove(peer.Id);
            }

            nextInvitation.Remove(peer.Id);
            if (intentional)
            {
                recentPlayers.Remove(peer.Id);
                disconnectedSessions.Remove(peer.Id);
            }
            else
            {
                disconnectedSessions[peer.Id] = Now + TransportLimits.ReconnectGrace;
            }

            RefreshFriends(peer.Id, presenceOnly: true);
            if (intentional && FindRoom(peer) is { } leaving)
            {
                Remove(leaving, peer.Id);
                peer.RoomCode = null;
                log.LogDebug("Window closed. Online={Online} Rooms={Rooms}", peers.Count, rooms.Count);
                return;
            }
            if (FindRoom(peer) is { } room && room.Members.TryGetValue(peer.Id, out var member))
            {
                member.Connected = false;
                member.DisconnectedAt = Now;
                TransferHost(room);
                BroadcastState(room);
            }
            log.LogDebug("Connection detached. Online={Online}", peers.Count);
        }
    }

    public void Revoke(Guid id)
    {
        lock (gate)
        {
            if (peers.TryGetValue(id, out var peer))
            {
                peer.Close();
                Detach(peer);
            }
        }
    }

    public void Handle(IGamePeer peer, ClientCommand command)
    {
        lock (gate)
        {
            if (!peers.TryGetValue(peer.Id, out var active) || !ReferenceEquals(active, peer))
            {
                return;
            }

            if (command.Type is not { Length: > 0 and <= 40 } || command.Type.Any(c => !char.IsAsciiLetterLower(c) && c != '-'))
            {
                Error(peer, "Command rejected.");
                return;
            }
            string rateKind = command.Type == "draw" && command.Draw is { } drawingOp
                ? drawingOp.Tool == "fill" ? "fill" : DrawingTools.Shape(drawingOp.Tool) ? "shape" : "draw" : command.Type;
            if (command.Type.StartsWith("leaderboard", StringComparison.Ordinal))
            {
                rateKind = "leaderboard";
            }

            if (!peer.Allow(rateKind))
            {
                Error(peer, "Slow down; message limit reached.", command.Type);
                return;
            }
            var room = FindRoom(peer);
            try
            {
                switch (command.Type)
                {
                    case "social":
                        SendSocial(peer);
                        break;
                    case "suggest":
                        SubmitSuggestion(peer, room, command);
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
                    case "like-drawing":
                        LikeDrawing(peer, room, command.TurnId);
                        break;
                    case "encore-pom":
                        TossPom(peer, room, command.TurnId);
                        break;
                    case "friend-request":
                        ChangeFriend(peer, command.TargetId, "request");
                        break;
                    case "friend-accept":
                        ChangeFriend(peer, command.TargetId, "accept");
                        break;
                    case "friend-remove":
                        ChangeFriend(peer, command.TargetId, "remove");
                        break;
                    case "view-profile":
                        ViewProfile(peer, command.TargetId);
                        break;
                    case "profile-privacy":
                        UpdatePrivacy(peer, room, command);
                        break;
                    case "invite":
                        Invite(peer, room, command.TargetId);
                        break;
                    case "accept-invite":
                        AcceptInvitation(peer, room, command.TargetId);
                        break;
                    case "dismiss-invite":
                        if (invitations.TryGetValue(peer.Id, out var inbox))
                        {
                            inbox.RemoveAll(i => i.View.Id == command.TargetId);
                        }

                        SendSocial(peer);
                        break;
                    case "list":
                        peer.Send(new("rooms", Rooms: rooms.Values.Where(r => r.Options.IsPublic)
                            .Select(r => new RoomSummary(r.Code, r.Options.Title,
                                r.Members.Count, r.Options.MaxPlayers, r.Phase)).ToArray()));
                        break;
                    case "create":
                        CreateRoom(peer, room, command);
                        break;
                    case "join":
                        JoinRoom(peer, room, command);
                        break;
                    case "leave":
                        LeaveRoom(peer, room, command);
                        break;
                    case "start":
                        StartMatch(peer, room, command);
                        break;
                    case "draw":
                        DrawOnCanvas(peer, room, command);
                        break;
                    case "choose-word":
                        ChooseWord(peer, room, command);
                        break;
                    case "choose-custom-word":
                        ChooseCustomWord(peer, room, command);
                        break;
                    case "configure":
                        ConfigureRoom(peer, room, command);
                        break;
                    case "cursor":
                        MoveCanvasCursor(peer, room, command);
                        break;
                    case "undo":
                        UndoDrawing(peer, room, command);
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
                        UpdateProfile(peer, room, command);
                        break;
                    case "kick":
                        KickPlayer(peer, room, command);
                        break;
                    case "sync":
                        if (room is not null)
                        {
                            State(room, peer, snapshot: true);
                        }

                        break;
                    case "ping":
                        peer.Send(new("pong"));
                        break;
                    default:
                        Error(peer, "Unknown command; check the plugin and server versions.");
                        break;
                }
            }
            catch (ArgumentException ex) { Error(peer, ex.Message, command.Type); }
        }
    }

    public void Tick()
    {
        lock (gate)
        {
            ExpireDisconnectedSessions();
            CleanInvitationInboxes();
            foreach (var room in rooms.Values.ToArray())
            {
                foreach (var member in room.Members.Values.Where(m => !m.Connected && Now - m.DisconnectedAt >= TransportLimits.ReconnectGrace).ToArray())
                {
                    Remove(room, member.Id);
                    recentPlayers.Remove(member.Id);
                }
                if (!rooms.ContainsKey(room.Code))
                {
                    continue;
                }

                if (room.Members.Values.All(m => !m.Connected))
                {
                    if (room.EmptySince == default)
                    {
                        room.EmptySince = Now;
                    }

                    if (Now - room.EmptySince > TimeSpan.FromSeconds(60))
                    {
                        rooms.Remove(room.Code);
                    }

                    continue;
                }
                room.EmptySince = default;
                if (room.Vote is { } ballot && Now >= ballot.Expires)
                {
                    room.Vote = null;
                    BroadcastState(room);
                }
                if (room.SkipVote is { } skipBallot && Now >= skipBallot.Expires)
                {
                    room.SkipVote = null;
                    BroadcastState(room);
                }
                if (room.Phase == "choosing")
                {
                    if (room.Drawer is null || !room.Members.TryGetValue(room.Drawer.Value, out var choosing) || !choosing.Connected)
                    {
                        EndTurn(room);
                    }
                    else if (Now >= room.Deadline)
                    {
                        BeginDrawing(room, room.Choices[RandomNumberGenerator.GetInt32(room.Choices.Length)]);
                    }
                }
                if (room.Phase == "drawing")
                {
                    if (Now >= room.Deadline || room.Drawer is null ||
                        !room.Members.TryGetValue(room.Drawer.Value, out var drawer) || !drawer.Connected)
                    {
                        EndTurn(room);
                        continue;
                    }
                    int elapsed = (int)Math.Max(0, room.Options.TurnSeconds - (room.Deadline - Now).TotalSeconds);
                    int reveal = room.Options.HintSeconds <= 0 ? 0 : elapsed / room.Options.HintSeconds;
                    reveal = Math.Min(reveal, room.Word.Count(char.IsLetterOrDigit) / 2);
                    if (reveal != room.Revealed)
                    {
                        room.Revealed = reveal;
                        BroadcastState(room);
                    }
                }
                else if (room.Phase == "intermission" && Now >= room.Deadline)
                {
                    NextTurn(room);
                }
            }
        }
    }

    private void Remove(Room room, Guid id)
    {
        room.Members.Remove(id);
        if (peers.TryGetValue(id, out var peer))
        {
            peer.RoomCode = null;
        }

        if (room.Members.Count == 0)
        {
            rooms.Remove(room.Code);
            return;
        }
        TransferHost(room);
        if (room.Phase is "drawing" or "choosing" && room.Drawer == id)
        {
            EndTurn(room);
        }

        BroadcastState(room);
    }

    private static void TransferHost(Room room)
    {
        if (!room.Members.TryGetValue(room.Host, out var host) || !host.Connected)
        {
            room.Host = room.Members.Values.FirstOrDefault(m => m.Connected)?.Id ?? room.Members.Keys.First();
        }

        if (room.Vote?.Target == room.Host)
        {
            room.Vote = null;
        }
    }

    private Room? FindRoom(IGamePeer peer) => peer.RoomCode is { } code ? rooms.GetValueOrDefault(code) : null;
    private static void Error(IGamePeer peer, string message, string? command = null) => peer.Send(new("error", Error: message, RejectedCommand: command));
    private static void RequireHost(IGamePeer peer, Room? room)
    {
        if (room is null || room.Host != peer.Id)
        {
            throw new ArgumentException("Only the room host can do that.");
        }
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
            room.TurnId, room.Round,
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
            room.DrawingLikes.Count, room.DrawingLikes.Contains(peer.Id), room.Effect, room.Crayons,
            room.EncoreUntil, room.PomSenders.Count, CanTossPom(room, peer.Id)),
            HostWords: room.Host == peer.Id && room.Phase is "waiting" or "results" ? (string[])room.HostWords.Clone() : null));
    }

    private void BroadcastState(Room room, bool reset = false, Guid? except = null)
    {
        var players = PlayerViews(room);
        foreach (var member in room.Members.Values)
        {
            if (member.Id != except && member.Connected && peers.TryGetValue(member.Id, out var peer))
            {
                State(room, peer, reset: reset, players: players);
            }
        }
    }

    private void Broadcast(Room room, ServerEvent message)
    {
        // These messages are identical for every recipient. Private state and
        // done-mode chat deliberately use their own per-recipient paths.
        byte[] bytes = Protocol.Encode(message);
        foreach (var member in room.Members.Values)
        {
            if (member.Connected && peers.TryGetValue(member.Id, out var peer))
            {
                peer.SendEncoded(message, bytes);
            }
        }
    }

    private void SystemMessage(Room room, string text) => Broadcast(room, new("chat", Chat: new(null, "Kupopop", text, System: true)));

    private RoomOptions Validate(RoomOptions options, string[]? custom = null)
    {
        string title = options.Title?.Trim() ?? "";
        if (!Protocol.ValidName(title))
        {
            throw new ArgumentException("Room title must be 2–40 characters.");
        }

        if ((options.Effects & ~RoomEffect.All) != 0 || options.TurnSeconds is < 15 or > 300 || options.Rounds is < 1 or > 5 ||
            options.MaxPlayers is < 2 or > 16 || options.HintSeconds is < 0 or > 300 || options.WordChoiceCount is < 1 or > 8)
        {
            throw new ArgumentException("Room settings are outside supported limits.");
        }

        string[] selection = words.Selection(options.WordLists, allowEmpty: custom is { Length: > 0 });
        words.Choices(selection, "", options.WordChoiceCount, custom);
        return options with
        {
            Title = title,
            WordLists = selection,
            CustomWordCount = custom?.Length ?? 0
        };
    }

    private void VoteKick(IGamePeer peer, Room? room, Guid turn, bool skip)
    {
        if (room is not null && !(skip ? room.Options.VoteSkipEnabled : room.Options.VoteKickEnabled))
        {
            throw new ArgumentException("This room has disabled that vote option.");
        }

        if (room?.Phase != "drawing" || turn != room.TurnId || Now >= room.Deadline ||
            room.Drawer is not { } target || target == peer.Id || !room.Members[peer.Id].Connected)
        {
            throw new ArgumentException("You may only vote on the current drawer.");
        }

        var activeVote = skip ? room.SkipVote : room.Vote;
        if (!skip && target == room.Host)
        {
            throw new ArgumentException("The room leader cannot be vote-kicked.");
        }

        if (activeVote is null)
        {
            if (skip ? room.SkipVoteUsed : room.VoteUsed)
            {
                throw new ArgumentException("A vote has already ended this turn.");
            }

            var online = room.Members.Values.Where(m => m.Connected).ToArray();
            Guid[] eligible = online.Where(m => m.Id != target).Select(m => m.Id).ToArray();
            int required = online.Length < 5 ? eligible.Length : (int)Math.Ceiling(eligible.Length * 0.7);
            activeVote = new(target, eligible, required, Now.AddSeconds(30));
            if (skip)
            {
                room.SkipVote = activeVote;
                room.SkipVoteUsed = true;
            }
            else
            {
                room.Vote = activeVote;
                room.VoteUsed = true;
            }
        }
        var ballot = activeVote;
        if (Now >= ballot.Expires || !ballot.Eligible.Contains(peer.Id) || !ballot.Votes.Add(peer.Id))
        {
            throw new ArgumentException("This vote has ended or your vote was already counted.");
        }

        int remaining = Math.Max(0, ballot.Required - ballot.Votes.Count);
        SystemMessage(room, $"Vote to {(skip ? "skip" : "kick")}: {ballot.Votes.Count}/{ballot.Required}. {remaining} more {(remaining == 1 ? "vote" : "votes")} needed.");
        if (ballot.Votes.Count >= ballot.Required)
        {
            if (skip)
            {
                EndTurn(room);
                SystemMessage(room, "The room voted to skip this turn.");
                return;
            }
            room.Vote = null;
            room.BannedAccounts.Add(target);
            Remove(room, target);
            if (peers.TryGetValue(target, out var removed))
            {
                SendSocial(removed);
                removed.Send(new("left", Error: "The room voted to ban you from this room."));
            }
            SystemMessage(room, "The drawer was banned from this room by vote.");
            log.LogInformation("Drawer removed by room vote. Votes={Votes} Required={Required}", ballot.Votes.Count, ballot.Required);
        }
        else
        {
            BroadcastState(room);
        }
    }

}
