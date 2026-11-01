using System.Text;
using System.Text.Json;
using Kupopop.Server;
using Kupopop.Shared;
using Microsoft.Extensions.Logging.Abstractions;

static class RoundFlowChecks
{
    public static void Run(string working, Action<bool, string> check)
    {
        var accounts = new AccountStore(Path.Combine(working, "round-flow"));
        var identities = Enumerable.Range(0, 6).Select(i => accounts.Create("Round artist " + i)).ToArray();
        var bank = new WordBank([new("test", "Test bank", true, false,
            ["Alpha lantern", "Bravo needle", "Charlie pencil", "Delta canvas", "Echo ribbon", "Foxtrot sketch"])]);
        (GameEngine game, Clock clock, Peer[] peers) Room(int count = 6)
        {
            var clock = new Clock(); var game = new GameEngine(accounts, NullLogger<GameEngine>.Instance, clock, bank);
            var peers = identities.Take(count).Select(c => new Peer(c.AccountId)).ToArray();
            foreach (var peer in peers) game.Attach(peer);
            game.Handle(peers[0], new("create", Options: new("Round tests", HintSeconds: 1)));
            for (int i = 1; i < count; i++) game.Handle(peers[i], new("join", Text: peers[0].State.Code));
            game.Handle(peers[0], new("start"));
            return (game, clock, peers);
        }
        void Choose(GameEngine game, Peer drawer) => game.Handle(drawer, new("choose-word", Text: drawer.State.WordChoices![0], TurnId: drawer.State.TurnId));

        var selection = Room(); var lead = selection.peers[0]; var initial = lead.State;
        check(initial.Phase == "choosing" && initial.WordChoices is { Length: 4 } && initial.WordChoices.Distinct().Count() == 4,
            "drawer receives four distinct server-selected words before drawing starts");
        check(selection.peers.Skip(1).All(p => p.State.WordChoices is null && p.State.Word is null &&
            initial.WordChoices!.All(w => !Encoding.UTF8.GetString(Protocol.Encode(p.State)).Contains(w))),
            "guesser state and serialized packets contain none of the offered words");
        selection.game.Handle(selection.peers[1], new("choose-word", Text: initial.WordChoices![0], TurnId: initial.TurnId));
        check(selection.peers[1].Events.Last().Type == "error" && lead.State.Phase == "choosing", "non-drawer cannot choose an answer");
        selection.game.Handle(lead, new("choose-word", Text: "not offered", TurnId: initial.TurnId));
        check(lead.Events.Last().Type == "error", "drawer cannot inject words outside their four offers");
        selection.game.Handle(lead, new("choose-word", Text: initial.WordChoices[0], TurnId: Guid.NewGuid()));
        check(lead.Events.Last().Type == "error", "stale word choices cannot affect a different turn");
        selection.game.Handle(lead, new("draw", Draw: new("line", 0, 0, 20, 20), TurnId: initial.TurnId));
        check(lead.Events.Last().Type == "error", "drawing is blocked until a word is selected");
        selection.clock.Advance(12);
        Choose(selection.game, lead);
        check(lead.State.Phase == "drawing" && lead.State.Deadline - selection.clock.GetUtcNow() == TimeSpan.FromSeconds(90) &&
            lead.State.WordChoices is null && lead.State.Word == initial.WordChoices[0],
            "choosing starts a fresh full drawing timer and clears unused offers");
        check(selection.peers.Skip(1).All(p => p.State.Word is null && p.State.WordChoices is null), "selected answer stays private from unsolved players");
        var timeout = Room(2); var offered = timeout.peers[0].State.WordChoices!;
        timeout.clock.Advance(15.1); timeout.game.Tick();
        check(timeout.peers[0].State.Phase == "drawing" && offered.Contains(timeout.peers[0].State.Word!) &&
            timeout.peers[0].State.Deadline - timeout.clock.GetUtcNow() == TimeSpan.FromSeconds(90), "expired selection auto-picks an offered word without reducing drawing time");
        var reconnect = Room(2); var original = reconnect.peers[0].State.WordChoices!;
        var replacement = new Peer(reconnect.peers[0].Id); reconnect.game.Detach(reconnect.peers[0]); reconnect.game.Attach(replacement);
        check(replacement.State.WordChoices!.SequenceEqual(original), "reconnecting drawer receives the same four choices rather than a fresh word bank");

        var positions = new HashSet<int>();
        for (int i = 0; i < 24; i++)
        {
            var hints = Room(2); Choose(hints.game, hints.peers[0]);
            hints.clock.Advance(1.1); hints.game.Tick();
            string first = hints.peers[1].State.Hint;
            int position = Enumerable.Range(0, first.Length).Single(p => char.IsLetterOrDigit(first[p])); positions.Add(position);
            hints.clock.Advance(1.1); hints.game.Tick();
            check(hints.peers[1].State.Hint[position] == first[position] && hints.peers[1].State.Hint.Count(char.IsLetterOrDigit) == 2,
                "random hints retain previous reveals without leaking extra letters");
        }
        check(positions.Count > 1 && positions.Any(i => i > 0), "hint positions vary across turns instead of always revealing the prefix");
        string starterRoot = Path.Combine(working, "starter-upgrade"); Directory.CreateDirectory(starterRoot);
        File.WriteAllBytes(Path.Combine(starterRoot, "word-lists.json"), JsonSerializer.SerializeToUtf8Bytes(new[] {
            new ServerWordList("starter", "FFXIV Starter", true, false, ["Moogle", "Chocobo", "Cactuar"]) }, Protocol.Json));
        check(WordBank.Open(starterRoot).Catalog.Single().Count == 4 && WordBank.Open(starterRoot).Choices(null, "").Length == 4,
            "untouched legacy starter banks upgrade atomically to support four offers");
        string customRoot = Path.Combine(working, "custom-bank-preservation"); Directory.CreateDirectory(customRoot);
        byte[] custom = JsonSerializer.SerializeToUtf8Bytes(new[] { new ServerWordList("custom", "Custom bank", true, false, ["Alpha", "Beta", "Gamma"]) }, Protocol.Json);
        File.WriteAllBytes(Path.Combine(customRoot, "word-lists.json"), custom); WordBank.Open(customRoot);
        check(File.ReadAllBytes(Path.Combine(customRoot, "word-lists.json")).SequenceEqual(custom), "starter migration never replaces custom word-bank files");

        selection.game.Handle(selection.peers[2], new("chat", Text: lead.State.Word));
        selection.game.Handle(selection.peers[2], new("leave"));
        selection.game.Handle(selection.peers[1], new("chat", Text: lead.State.Word));
        selection.game.Handle(selection.peers[3], new("chat", Text: lead.State.Word));
        check(selection.peers[4].State.FirstGuessers is null, "turn recap is withheld until the answer reveal");
        selection.clock.Advance(91); selection.game.Tick();
        var winners = selection.peers[4].State.FirstGuessers!;
        check(winners.Select(p => p.PlayerId).SequenceEqual(new[] { selection.peers[2].Id, selection.peers[1].Id, selection.peers[3].Id }) &&
            winners.Select(p => p.Points).SequenceEqual(new[] { 1000, 940, 880 }), "recap preserves the first three solve positions and awards even when the first solver leaves");
        check(selection.peers[4].State.Phase == "intermission" && selection.peers[4].State.Word == lead.State.Word &&
            selection.peers[4].State.Deadline - selection.clock.GetUtcNow() == TimeSpan.FromSeconds(6), "turn reveal exposes the answer and podium for six seconds");

        var skip = Room(3); Choose(skip.game, skip.peers[0]); var turn = skip.peers[0].State.TurnId;
        skip.game.Handle(skip.peers[1], new("vote-skip", TurnId: turn));
        check(skip.peers[0].Events.Any(e => e.Chat is { System: true, Text: "Vote to skip: 1/2. 1 more vote needed." }),
            "skip votes announce the correct singular remaining-vote count to the room");
        check(skip.peers[1].State.SkipVote is { Votes: 1, Required: 2 } && skip.peers[1].State.KickVote is null, "skip ballot is separate from removal voting");
        skip.game.Handle(skip.peers[1], new("vote-skip", TurnId: turn));
        check(skip.peers[1].Events.Last().Type == "error", "duplicate skip votes cannot inflate agreement");
        skip.game.Handle(skip.peers[2], new("vote-skip", TurnId: turn));
        check(skip.peers[0].State.Phase == "intermission" && skip.peers.All(p => p.RoomCode is not null) &&
            skip.peers[0].State.Players.All(p => p.Score == 0), "vote skip ends the turn without removing players or inventing score");
        skip.clock.Advance(6.1); skip.game.Tick();
        skip.game.Handle(skip.peers[0], new("kick", Text: skip.peers[1].Id.ToString()));
        check(skip.peers[1].RoomCode is null, "leader context action can remove the current drawer while choosing");

        var cursor = Room(3); Choose(cursor.game, cursor.peers[0]); var cursorTurn = cursor.peers[0].State.TurnId;
        var brush = new BrushCursor(20, 30, "circle", 32, 8);
        int cursorEvents = cursor.peers.Sum(p => p.Events.Count);
        cursor.game.Handle(cursor.peers[1], new("cursor", Cursor: brush, TurnId: cursorTurn));
        check(cursor.peers.Sum(p => p.Events.Count) == cursorEvents, "unauthorized cursor packets are silently discarded without broadcasts");
        cursor.game.Handle(cursor.peers[0], new("cursor", Cursor: brush, TurnId: cursorTurn));
        check(cursor.peers[2].Events.Last().Cursor == brush && cursor.peers[2].Events.Last().ActorId == cursor.peers[0].Id,
            "authorized cursor broadcasts carry canvas coordinates, tool, color and size");
        cursor.game.Handle(cursor.peers[0], new("cursor", Cursor: brush with { X = -1 }, TurnId: cursorTurn));
        check(cursor.peers[0].Events.Last().Type == "error", "cursor coordinates outside the canvas are rejected");
        cursor.clock.Advance(2.1); cursor.game.Handle(cursor.peers[2], new("sync"));
        check(cursor.peers[2].State.Cursor is null, "stale brush cursors expire from reconnection snapshots");
        cursor.clock.Advance(100); cursor.game.Tick();
        int endedEvents = cursor.peers.Sum(p => p.Events.Count);
        cursor.game.Handle(cursor.peers[0], new("cursor", Cursor: brush, TurnId: cursorTurn));
        check(cursor.peers.Sum(p => p.Events.Count) == endedEvents,
            "in-flight cursor packets after the turn ends are silently discarded");

        check(Palette.Opaque.Length == 32 && Palette.Transparent == 16 && Palette.Colors[0] == 0xFFFFFFFF && Palette.Colors[1] == 0xFF222222,
            "32-color palette preserves legacy colors and transparent index");
        foreach (string shape in new[] { "line", "rectangle", "square", "ellipse", "circle" })
        {
            var drawing = new PixelCanvas(64, 64); var undo = new CanvasHistory();
            undo.Capture(drawing, Guid.NewGuid());
            check(drawing.Apply(new(shape, 10, 10, 50, 40, 32, 2)) && drawing.Pixels.Any(p => p == 32) && drawing.Pixels.All(p => p != Palette.Transparent),
                "shape rasterizes with the expanded palette: " + shape);
            check(undo.Undo(drawing) && drawing.Pixels.All(p => p == 0), "whole-shape undo restores the previous canvas: " + shape);
        }
        var circle = new PixelCanvas(64, 64);
        circle.Apply(new("circle", 10, 10, 50, 50, 1, 1)); circle.Apply(new("fill", 30, 30, 30, 30, 18));
        check(circle.Pixels[30 * 64 + 30] == 18 && circle.Pixels[0] == 0, "circle outline is closed for deterministic flood fill");
        check(DrawingTools.Constrain(new("square", 50, 50, 10, 30)).X2 == 30 && DrawingTools.Constrain(new("circle", 10, 10, 50, 30)).X2 == 30,
            "circle and square constraints work in both drag directions");

        check(UpdateFeed.NewerVersion(Encoding.UTF8.GetBytes("[{\"InternalName\":\"Kupopop\",\"AssemblyVersion\":\"9.0.0.0\"}]"), new Version(1, 0, 0, 0)) == new Version(9, 0, 0, 0),
            "update notice detects a newer matching plugin version");
        check(UpdateFeed.NewerVersion(Encoding.UTF8.GetBytes("[{\"InternalName\":\"Other\",\"AssemblyVersion\":\"9.0\"},{\"InternalName\":\"Kupopop\",\"AssemblyVersion\":\"1.0.0.0\"}]"), new Version(1, 0, 0, 0)) is null,
            "unrelated plugins and current releases cannot trigger update toasts");
        bool rejected = false; try { UpdateFeed.NewerVersion(new byte[65537], new Version(1, 0)); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "update metadata has a strict size bound");
        var toastClock = new Clock(); var toasts = new ToastQueue(toastClock);
        toasts.Show("update", "Update", "A new version is ready"); toasts.Show("notice", "Saved", "Profile saved", TimeSpan.FromSeconds(2));
        toastClock.Advance(3);
        check(toasts.Visible().Length == 1 && toasts.Visible()[0].Id == "update", "temporary toasts expire while dismissible update notices persist");
        toasts.Dismiss("update"); check(toasts.Visible().Length == 0, "dismissed toast leaves the plugin notification stack");
        for (int i = 0; i < 10; i++) toasts.Show("same", "Title", "Message");
        check(toasts.Visible().Length == 1, "repeated notifications coalesce rather than stacking duplicates");
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(double seconds) => now = now.AddSeconds(seconds);
    }
    private sealed class Peer(Guid id) : IGamePeer
    {
        public Guid Id { get; } = id;
        public string? RoomCode { get; set; }
        public List<ServerEvent> Events = [];
        public RoomView State => Events.Last(e => e.Room is not null).Room!;
        public void Send(ServerEvent message) => Events.Add(message);
        public bool Allow(string type) => true;
        public void Close() { }
    }
}
