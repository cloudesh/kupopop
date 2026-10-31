using System.Security.Cryptography;
using System.Text;
using Kupopop.Shared;

namespace Kupopop.Server;

public sealed class GameEngine(AccountStore accounts, ILogger<GameEngine> log, TimeProvider clock, WordBank? wordBank = null)
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
        public string Code = code;
        public Guid Host = host;
        public RoomOptions Options = options;
        public Dictionary<Guid, Member> Members = [];
        public PixelCanvas Canvas = new();
        public CanvasHistory History = new();
        public KickBallot? Vote;
        public bool VoteUsed;
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
        }
    }

    public void Detach(IGamePeer peer)
    {
        lock (gate)
        {
            if (!peers.TryGetValue(peer.Id, out var active) || !ReferenceEquals(active, peer)) return;
            peers.Remove(peer.Id);
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
            string rateKind = command.Type == "draw" && command.Draw?.Tool == "fill" ? "fill" : command.Type;
            if (!peer.Allow(rateKind)) { Error(peer, "Slow down; message limit reached.", command.Type); return; }
            var room = FindRoom(peer);
            try
            {
                switch (command.Type)
                {
                    case "list":
                        peer.Send(new("rooms", Rooms: rooms.Values.Where(r => r.Options.IsPublic)
                            .Select(r => new RoomSummary(r.Code, r.Options.Title,
                                r.Members.Values.Count(m => m.Connected), r.Options.MaxPlayers, r.Phase)).ToArray()));
                        break;
                    case "create":
                        if (room is not null) throw new ArgumentException("Leave your current room first.");
                        if (rooms.Count >= 100) throw new ArgumentException("Server room capacity reached.");
                        var options = Validate(command.Options ?? new("A kupo drawing room"));
                        string code;
                        do code = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)); while (rooms.ContainsKey(code));
                        room = new(code, peer.Id, options);
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
                    case "undo":
                        if (room?.Phase != "drawing" || room.Drawer != peer.Id || command.TurnId != room.TurnId || Now >= room.Deadline)
                            throw new ArgumentException("Undo is only available during your current turn.");
                        if (!room.History.Undo(room.Canvas)) throw new ArgumentException("Nothing to undo.");
                        Broadcast(room, new("canvas", Canvas: (byte[])room.Canvas.Pixels.Clone(), TurnId: room.TurnId, CanUndo: room.History.CanUndo));
                        break;
                    case "vote-kick":
                        VoteKick(peer, room, command.TurnId);
                        break;
                    case "chat":
                        Chat(peer, room, command.Text ?? "");
                        break;
                    case "profile":
                        var profile = accounts.Update(peer.Id, command.Text, command.Avatar, command.ResetAvatar, command.AvatarPreset);
                        peer.Send(new("profile", Profile: profile));
                        if (room is not null) BroadcastState(room);
                        break;
                    case "kick":
                        RequireHost(peer, room);
                        if (!Guid.TryParse(command.Text, out var target) || target == peer.Id || !room!.Members.ContainsKey(target))
                            throw new ArgumentException("Select another player to remove.");
                        if (room.Phase == "drawing" && target == room.Drawer)
                            throw new ArgumentException("Use the vote to remove the current drawer.");
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
            foreach (var room in rooms.Values.ToArray())
            {
                foreach (var member in room.Members.Values.Where(m => !m.Connected && Now - m.DisconnectedAt > TimeSpan.FromSeconds(30)).ToArray())
                    Remove(room, member.Id);
                if (!rooms.ContainsKey(room.Code)) continue;
                if (room.Members.Values.All(m => !m.Connected))
                {
                    if (room.EmptySince == default) room.EmptySince = Now;
                    if (Now - room.EmptySince > TimeSpan.FromSeconds(60)) rooms.Remove(room.Code);
                    continue;
                }
                room.EmptySince = default;
                if (room.Vote is { } ballot && Now >= ballot.Expires) { room.Vote = null; BroadcastState(room); }
                if (room.Phase == "drawing")
                {
                    if (Now >= room.Deadline || room.Drawer is null ||
                        !room.Members.TryGetValue(room.Drawer.Value, out var drawer) || !drawer.Connected)
                    { EndTurn(room); continue; }
                    int elapsed = room.Options.TurnSeconds - (int)(room.Deadline - Now).TotalSeconds;
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
            if (Normalize(text) == Normalize(room.Word))
            {
                int rank = room.Members.Values.Count(m => m.Solved) + 1;
                member.Solved = true;
                int award = Math.Max(100, 1000 - (rank - 1) * 60);
                member.Score += award;
                if (room.Drawer is { } drawerId && room.Members.TryGetValue(drawerId, out var drawer)) drawer.Score += 100;
                SystemMessage(room, $"{accounts.Get(peer.Id)!.DisplayName} guessed the word! (+{award})");
                log.LogInformation("Correct guess accepted. Rank={Rank}", rank);
                if (room.Members.Values.Where(m => m.Connected && m.Id != room.Drawer).All(m => m.Solved)) EndTurn(room);
                else BroadcastState(room);
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
        room.Vote = null; room.VoteUsed = false; room.History.Clear();
        do room.Position++; while (room.Position < room.Deck.Count &&
            (!room.Members.TryGetValue(room.Deck[room.Position], out var member) || !member.Connected));
        if (room.Position >= room.Deck.Count || room.Members.Values.Count(m => m.Connected) < 2)
        {
            room.Phase = "results";
            room.Drawer = null;
            room.Deadline = default;
            SystemMessage(room, "Match finished! The host can start another match.");
            BroadcastState(room);
            log.LogInformation("Match finished. Players={Players}", room.Members.Count);
            return;
        }
        room.Phase = "drawing";
        room.Drawer = room.Deck[room.Position];
        room.TurnId = Guid.NewGuid();
        room.Deadline = Now.AddSeconds(room.Options.TurnSeconds);
        room.Revealed = 0;
        room.Word = words.Choose(room.Options.WordLists, room.LastWord);
        room.LastWord = room.Word;
        room.Canvas.Clear();
        foreach (var member in room.Members.Values) member.Solved = false;
        BroadcastState(room, reset: true);
        SystemMessage(room, "A new drawing turn has started.");
        log.LogInformation("Drawing turn started. Round={Round} Players={Players}", room.Position / room.RotationSize + 1, room.Members.Count);
    }

    private void EndTurn(Room room)
    {
        if (room.Phase != "drawing") return;
        room.Vote = null; room.History.Clear();
        room.Phase = "intermission";
        room.Deadline = Now.AddSeconds(4);
        SystemMessage(room, "Turn finished. The answer is shown above the canvas.");
        BroadcastState(room);
    }

    private void Remove(Room room, Guid id)
    {
        room.Members.Remove(id);
        if (peers.TryGetValue(id, out var peer)) peer.RoomCode = null;
        if (room.Members.Count == 0) { rooms.Remove(room.Code); return; }
        TransferHost(room);
        if (room.Phase == "drawing" && room.Drawer == id) EndTurn(room);
        BroadcastState(room);
    }

    private static void TransferHost(Room room)
    {
        if (!room.Members.TryGetValue(room.Host, out var host) || !host.Connected)
            room.Host = room.Members.Values.FirstOrDefault(m => m.Connected)?.Id ?? room.Members.Keys.First();
    }

    private Room? FindRoom(IGamePeer peer) => peer.RoomCode is { } code ? rooms.GetValueOrDefault(code) : null;
    private static void Error(IGamePeer peer, string message, string? command = null) => peer.Send(new("error", Error: message, RejectedCommand: command));
    private static void RequireHost(IGamePeer peer, Room? room)
    {
        if (room is null || room.Host != peer.Id) throw new ArgumentException("Only the room host can do that.");
    }

    private void State(Room room, IGamePeer peer, bool snapshot = false, bool reset = false)
    {
        var member = room.Members.GetValueOrDefault(peer.Id);
        bool done = room.Phase == "drawing" && (room.Drawer == peer.Id || member?.Solved == true);
        bool showWord = done || room.Phase is "intermission" or "results";
        int letters = room.Revealed;
        string hint = string.Concat(room.Word.Select(c => !char.IsLetterOrDigit(c) ? c : letters-- > 0 ? c : '_'));
        peer.Send(new("state", Room: new(room.Code, room.Options, room.Phase, room.Host, room.Drawer,
            room.TurnId, room.RotationSize == 0 ? 0 : Math.Min(room.Options.Rounds, room.Position / room.RotationSize + 1),
            room.Deadline, hint, showWord ? room.Word : null, done,
            room.Members.Values.Select(m => new PlayerView(accounts.Get(m.Id)!, m.Score, m.Connected,
                m.Solved, m.Id == room.Host, m.Id == room.Drawer)).ToArray(), Now,
            snapshot ? (byte[])room.Canvas.Pixels.Clone() : null, reset,
            room.Phase == "drawing" && room.Drawer == peer.Id && room.History.CanUndo,
            room.Vote is { } vote ? new(vote.Target, vote.Votes.Count, vote.Required, vote.Votes.Contains(peer.Id), vote.Expires) : null)));
    }

    private void BroadcastState(Room room, bool reset = false, Guid? except = null)
    {
        foreach (var member in room.Members.Values)
            if (member.Id != except && member.Connected && peers.TryGetValue(member.Id, out var peer)) State(room, peer, reset: reset);
    }

    private void Broadcast(Room room, ServerEvent message)
    {
        foreach (var member in room.Members.Values)
            if (member.Connected && peers.TryGetValue(member.Id, out var peer)) peer.Send(message);
    }

    private void SystemMessage(Room room, string text) => Broadcast(room, new("chat", Chat: new(null, "Kupopop", text, System: true)));

    private RoomOptions Validate(RoomOptions options)
    {
        string title = options.Title?.Trim() ?? "";
        if (!Protocol.ValidName(title)) throw new ArgumentException("Room title must be 2–40 characters.");
        if (options.TurnSeconds is < 15 or > 300 || options.Rounds is < 1 or > 5 ||
            options.MaxPlayers is < 2 or > 16 || options.HintSeconds is < 0 or > 300)
            throw new ArgumentException("Room settings are outside supported limits.");
        return options with { Title = title, WordLists = words.Selection(options.WordLists) };
    }

    private void VoteKick(IGamePeer peer, Room? room, Guid turn)
    {
        if (room?.Phase != "drawing" || turn != room.TurnId || Now >= room.Deadline ||
            room.Drawer is not { } target || target == peer.Id || !room.Members[peer.Id].Connected)
            throw new ArgumentException("You may only vote on the current drawer.");
        if (room.Vote is null)
        {
            if (room.VoteUsed) throw new ArgumentException("A vote has already ended this turn.");
            var online = room.Members.Values.Where(m => m.Connected).ToArray();
            Guid[] eligible = online.Where(m => m.Id != target).Select(m => m.Id).ToArray();
            int required = online.Length < 5 ? eligible.Length : (int)Math.Ceiling(eligible.Length * 0.7);
            room.Vote = new(target, eligible, required, Now.AddSeconds(30)); room.VoteUsed = true;
        }
        var ballot = room.Vote;
        if (Now >= ballot.Expires || !ballot.Eligible.Contains(peer.Id) || !ballot.Votes.Add(peer.Id))
            throw new ArgumentException("This vote has ended or your vote was already counted.");
        if (ballot.Votes.Count >= ballot.Required)
        {
            room.Vote = null;
            Remove(room, target);
            if (peers.TryGetValue(target, out var removed)) removed.Send(new("left", Error: "The room voted to remove you."));
            SystemMessage(room, "The drawer was removed by vote.");
            log.LogInformation("Drawer removed by room vote. Votes={Votes} Required={Required}", ballot.Votes.Count, ballot.Required);
        }
        else BroadcastState(room);
    }

    private static string Normalize(string value)
    {
        var result = new StringBuilder();
        foreach (char c in value.Normalize(NormalizationForm.FormD))
            if (char.IsLetterOrDigit(c)) result.Append(char.ToUpperInvariant(c));
        return result.ToString();
    }
}
