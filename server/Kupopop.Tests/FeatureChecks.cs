using System.Text;
using System.Text.Json;
using Kupopop.Shared;
using Kupopop.Server;
using Microsoft.Extensions.Logging.Abstractions;

static class FeatureChecks
{
    public static void Run(string working, Action<bool, string> check)
    {
        var portrait = new PixelCanvas(64, 64, transparency: true);
        portrait.Load(AvatarGenerator.Generate(123));
        check(portrait.Pixels[0] == Palette.Transparent && portrait.Rgba()[3] == 0,
            "reset/default portrait contains actual transparent pixels in the editable canvas");
        portrait.Apply(new("pencil", 1, 1, 2, 1, 4));
        portrait.Apply(new("eraser", 1, 1, 2, 1));
        check(portrait.Pixels[65] == Palette.Transparent, "portrait eraser removes pixels to transparency");
        portrait.Apply(new("fill", 0, 0, 0, 0, 5));
        portrait.Apply(new("fill", 0, 0, 0, 0, Palette.Transparent));
        check(portrait.Pixels[0] == Palette.Transparent && portrait.Pixels[34 * 64 + 32] == 0,
            "transparent fill preserves opaque white facial details");
        check(!new PixelCanvas().Apply(new("pencil", 1, 1, 2, 1, Palette.Transparent)),
            "transparent avatar palette cannot be used to inject alpha into the game canvas");
        var history = new CanvasHistory(2);
        byte[] original = (byte[])portrait.Pixels.Clone();
        Guid stroke = Guid.NewGuid();
        history.Capture(portrait, stroke); portrait.Apply(new("pencil", 10, 10, 15, 10, 1));
        history.Capture(portrait, stroke); portrait.Apply(new("pencil", 15, 10, 20, 10, 1));
        check(history.Undo(portrait) && portrait.Pixels.SequenceEqual(original) && !history.CanUndo,
            "portrait undo removes an entire segmented stroke and restores its alpha pixels");
        for (int i = 0; i < 5; i++) { history.Capture(portrait, Guid.NewGuid()); portrait.Apply(new("pencil", i, 0, i, 0, 1)); }
        check(history.Undo(portrait) && history.Undo(portrait) && !history.Undo(portrait), "undo history has a strict memory bound");

        var storePath = Path.Combine(working, "features");
        var store = new AccountStore(storePath);
        var account = store.Create("Portrait tester");
        store.Update(account.AccountId, null, portrait.Pixels, false);
        var persisted = new AccountStore(storePath).Get(account.AccountId)!;
        check(persisted.AvatarPixels!.SequenceEqual(portrait.Pixels), "custom alpha pixels survive server persistence");
        bool rejected = false;
        try { store.Update(account.AccountId, null, null, false, "job-paladin"); } catch (ArgumentException) { rejected = true; }
        check(rejected, "removed job presets cannot be selected through a forged command");
        rejected = false;
        try { store.Update(account.AccountId, null, null, false, "arbitrary-remote-image"); } catch (ArgumentException) { rejected = true; }
        check(rejected, "arbitrary portrait paths and non-catalog presets are rejected");
        check(AvatarPresets.Items.Length == 7 && AvatarPresets.Items.Select(p => p.Id).Distinct().Count() == AvatarPresets.Items.Length,
            "only the seven native sketch presets remain available");
        store.Update(account.AccountId, null, new byte[4096], false);
        check(store.Get(account.AccountId) is { AvatarPreset: null } && store.Get(account.AccountId)!.AvatarPixels!.All(p => p == 0),
            "switching from a preset to a legacy white custom drawing preserves its pixels");
        store.Update(account.AccountId, null, null, true);
        check(store.Get(account.AccountId) is { AvatarPreset: null, AvatarPixels: null }, "default reset clears prior custom pixels and preset selection");

        var bank = new WordBank([
            new("normal", "Normal test", true, false, ["Private alpha", "Private beta"]),
            new("complex", "Complex test", false, true, ["Hidden gamma", "Hidden delta", "Hidden epsilon", "Hidden zeta"])]);
        check(bank.Selection(null).SequenceEqual(new[] { "normal" }) && bank.Catalog[1].Complex && !bank.Catalog[1].DefaultEnabled,
            "standard lists default on and complex lists default off");
        string metadata = Encoding.UTF8.GetString(Protocol.Encode(new ServerEvent("wordlists", WordLists: bank.Catalog)));
        check(!metadata.Contains("Private alpha") && !metadata.Contains("Hidden gamma") && bank.Catalog[0].Count == 2,
            "catalog protocol reveals list metadata/counts without any answer bank");
        check(Enumerable.Range(0, 50).All(_ => bank.Choices(["complex"], "Hidden gamma", 1)[0].StartsWith("Hidden ") && bank.Choices(["complex"], "Hidden gamma", 1)[0] != "Hidden gamma"),
            "selected lists constrain server words and immediate repeats are avoided");
        foreach (var selection in new[] { Array.Empty<string>(), new[] { "unknown" }, new[] { "normal", "normal" } })
        {
            rejected = false; try { bank.Selection(selection); } catch (ArgumentException) { rejected = true; }
            check(rejected, "invalid, empty or duplicate word-list selection is rejected");
        }
        string bankDirectory = Path.Combine(working, "word-bank"); Directory.CreateDirectory(bankDirectory);
        File.WriteAllBytes(Path.Combine(bankDirectory, "word-lists.json"), JsonSerializer.SerializeToUtf8Bytes(new[] {
            new ServerWordList("private", "Private bank", true, false, ["Unpublished one", "Unpublished two"]) }, Protocol.Json));
        check(WordBank.Open(bankDirectory).Catalog.Single().Count == 2, "host-provided private word file loads independently of compiled binaries");

        var credentials = Enumerable.Range(0, 16).Select(i => store.Create("Vote tester " + i)).ToArray();
        (GameEngine engine, FeatureClock time, FeaturePeer[] peers) Room(int count)
        {
            var time = new FeatureClock(); var engine = new GameEngine(store, NullLogger<GameEngine>.Instance, time, bank);
            var peers = credentials.Take(count).Select(c => new FeaturePeer(c.AccountId)).ToArray();
            foreach (var p in peers) engine.Attach(p);
            engine.Handle(peers[0], new("create", Options: new("Feature room", WordLists: ["complex"])));
            for (int i = 1; i < count; i++) engine.Handle(peers[i], new("join", Text: peers[0].State.Code));
            engine.Detach(peers[0]); engine.Attach(peers[0]);
            engine.Handle(peers[1], new("start"));
            engine.Handle(peers[0], new("choose-word", Text: peers[0].State.WordChoices![0], TurnId: peers[0].State.TurnId));
            return (engine, time, peers);
        }
        var undo = Room(3); var drawer = undo.peers[0]; Guid turn = drawer.State.TurnId;
        var operation = new DrawOperation("pencil", 1, 1, 10, 1, 4, StrokeId: Guid.NewGuid());
        undo.engine.Handle(drawer, new("draw", Draw: operation, TurnId: turn));
        undo.engine.Handle(drawer, new("draw", Draw: operation with { X1 = 10, X2 = 20 }, TurnId: turn));
        undo.engine.Handle(undo.peers[1], new("undo", TurnId: turn));
        check(undo.peers[1].Events.Last().Type == "error", "spectators cannot undo the drawer's canvas");
        undo.engine.Handle(drawer, new("undo", TurnId: Guid.NewGuid()));
        check(drawer.Events.Last().Type == "error", "stale undo cannot affect the next turn");
        undo.engine.Handle(drawer, new("undo", TurnId: turn));
        check(undo.peers.All(p => p.Events.Last().Type == "canvas" && p.Events.Last().Canvas!.All(c => c == 0)),
            "server-authoritative whole-stroke undo sends the same clean snapshot to every room member");
        undo.time.Advance(91); undo.engine.Handle(drawer, new("undo", TurnId: turn));
        check(drawer.Events.Last().Type == "error", "undo after the server deadline is rejected");
        foreach (int population in new[] { 2, 3, 4, 5, 6, 16 })
        {
            var test = Room(population); Guid voteTurn = test.peers[0].State.TurnId;
            test.engine.Handle(test.peers[0], new("vote-kick", TurnId: voteTurn));
            check(test.peers[0].Events.Last().Type == "error", "drawer cannot vote on their own removal");
            int required = population < 5 ? population - 1 : (int)Math.Ceiling((population - 1) * 0.7);
            for (int i = 1; i <= required; i++)
            {
                test.engine.Handle(test.peers[i], new("vote-kick", TurnId: voteTurn));
                if (i < required) check(test.peers[i].State.KickVote?.Required == required && test.peers[0].RoomCode is not null,
                    "kick threshold is enforced before removal for population " + population);
            }
            check(test.peers[0].RoomCode is null && test.peers[0].Events.Last().Type == "left", "vote removes current drawer at the correct threshold for population " + population);
        }
        var expiry = Room(4); Guid expiryTurn = expiry.peers[0].State.TurnId;
        var promotion = Room(3);
        promotion.engine.Handle(promotion.peers[1], new("vote-kick", TurnId: promotion.peers[0].State.TurnId));
        promotion.engine.Detach(promotion.peers[1]);
        check(promotion.peers[0].State.HostId == promotion.peers[0].Id && promotion.peers[0].State.KickVote is null && !promotion.peers[2].State.KickVoteAvailable,
            "promotion to room leader cancels a removal ballot against the new leader");
        expiry.engine.Handle(expiry.peers[1], new("vote-kick", TurnId: expiryTurn));
        check(expiry.peers[2].Events.Any(e => e.Chat is { System: true, Text: "Vote to kick: 1/3. 2 more votes needed." }),
            "accepted kick votes announce the server-owned tally and remaining agreement in room chat");
        int voteMessages = expiry.peers[2].Events.Count(e => e.Chat?.Text.StartsWith("Vote to kick:") == true);
        expiry.engine.Handle(expiry.peers[1], new("vote-kick", TurnId: expiryTurn));
        check(expiry.peers[2].Events.Count(e => e.Chat?.Text.StartsWith("Vote to kick:") == true) == voteMessages,
            "rejected duplicate votes do not spam room vote announcements");
        check(expiry.peers[1].Events.Last().Type == "error" && expiry.peers[2].State.KickVote?.Votes == 1, "duplicate votes cannot inflate the tally");
        expiry.engine.Detach(expiry.peers[3]);
        expiry.engine.Handle(expiry.peers[2], new("vote-kick", TurnId: expiryTurn));
        check(expiry.peers[2].State.KickVote is { Votes: 2, Required: 3 } && expiry.peers[0].RoomCode is not null,
            "a disconnect cannot lower an active ballot threshold");
        expiry.time.Advance(30.1); expiry.engine.Tick();
        check(expiry.peers[1].State.KickVote is null, "unpassed votes expire after thirty seconds");
        expiry.engine.Handle(expiry.peers[1], new("vote-kick", TurnId: expiryTurn));
        check(expiry.peers[1].Events.Last().Type == "error", "expired ballots cannot be restarted repeatedly within one turn");
    }

    private sealed class FeatureClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(double seconds) => now = now.AddSeconds(seconds);
    }
    private sealed class FeaturePeer(Guid id) : IGamePeer
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
