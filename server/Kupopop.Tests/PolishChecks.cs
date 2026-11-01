using System.Text;
using Kupopop.Server;
using Kupopop.Shared;
using Microsoft.Extensions.Logging.Abstractions;

static class PolishChecks
{
    public static void Run(string working, Action<bool, string> check)
    {
        var accounts = new AccountStore(Path.Combine(working, "polish"));
        var identities = Enumerable.Range(0, 3).Select(i => accounts.Create("Polish artist " + i)).ToArray();
        var bank = new WordBank([
            new("life", "Everyday things", true, false, ["Aeroplane", "Paintbrush", "Umbrella", "Bicycle"], "General",
                new() { ["Aeroplane"] = ["Airplane", "Aeroplanes"], ["Bicycle"] = ["Bike"] }),
            new("xiv", "XIV creatures", false, false, ["Moogle", "Chocobo"], "FFXIV")]);
        (GameEngine game, Clock clock, Peer[] peers) Room(int roll = 0, bool enabled = true)
        {
            var clock = new Clock();
            var game = new GameEngine(accounts, NullLogger<GameEngine>.Instance, clock, bank, () => roll);
            var peers = identities.Select(id => new Peer(id.AccountId)).ToArray();
            foreach (var peer in peers) game.Attach(peer);
            game.Handle(peers[0], new("create", Options: new("Polish room", Rounds: 1, CustomWordsEnabled: enabled)));
            foreach (var peer in peers.Skip(1)) game.Handle(peer, new("join", Text: peers[0].State.Code));
            game.Handle(peers[0], new("start"));
            return (game, clock, peers);
        }
        foreach (int roll in new[] { 0, 9, 10, 99 })
        {
            var test = Room(roll);
            check(test.peers[0].State.CustomWordAvailable == (roll < 10), "rare custom words use the exact ten-percent roll boundary");
            check(test.peers.Skip(1).All(p => !p.State.CustomWordAvailable), "rare offer eligibility remains private to its drawer");
        }
        var rare = Room(); var lead = rare.peers[0]; var turn = lead.State.TurnId;
        void Custom(Peer peer, string word, Guid? id = null) => rare.game.Handle(peer, new("choose-custom-word", Text: word, TurnId: id ?? turn));
        Custom(rare.peers[1], "Paper lantern");
        check(rare.peers[1].Events.Last().Type == "error" && lead.State.Phase == "choosing", "spectators cannot inject custom answers");
        Custom(lead, "Paper lantern", Guid.NewGuid());
        check(lead.Events.Last().Type == "error", "stale custom-word commands cannot affect another turn");
        foreach (string invalid in new[] { "x", "1234", "Paper\nlantern", "<script>", new string('a', 49), "\u200bInvisible" })
        {
            Custom(lead, invalid);
            check(lead.Events.Last().Type == "error" && lead.State.Phase == "choosing", "invalid custom answers are bounded and rejected without starting a turn");
        }
        rare.clock.Advance(8); Custom(lead, "Paper lantern");
        check(lead.State is { Phase: "drawing", Word: "Paper lantern", CustomWordAvailable: false } && lead.State.Deadline - rare.clock.GetUtcNow() == TimeSpan.FromSeconds(90),
            "a valid rare answer starts a full timer and consumes the offer");
        check(rare.peers.Skip(1).All(p => p.State.Word is null && !Encoding.UTF8.GetString(Protocol.Encode(p.State)).Contains("Paper lantern")),
            "custom answers remain private from unsolved players");
        Custom(lead, "Another word");
        check(lead.Events.Last().Type == "error" && lead.State.Word == "Paper lantern", "custom answer cannot be replaced once drawing starts");
        rare.game.Handle(rare.peers[1], new("chat", Text: "paper-lantern"));
        check(rare.peers[1].State.Players.Single(p => p.Profile.Id == rare.peers[1].Id).Solved, "custom words use ordinary authoritative guess scoring");

        foreach (var test in new[] { Room(0, false), Room(10) })
        {
            test.game.Handle(test.peers[0], new("choose-custom-word", Text: "Paper lantern", TurnId: test.peers[0].State.TurnId));
            check(test.peers[0].Events.Last().Type == "error" && test.peers[0].State.Phase == "choosing", "host-disabled and absent rare offers reject forged commands");
        }
        var expired = Room(); expired.clock.Advance(16);
        expired.game.Handle(expired.peers[0], new("choose-custom-word", Text: "Paper lantern", TurnId: expired.peers[0].State.TurnId));
        check(expired.peers[0].Events.Last().Type == "error", "expired custom offers are rejected before the next tick");
        expired.game.Tick();
        check(expired.peers[0].State.Phase == "drawing" && !expired.peers[0].State.CustomWordAvailable && expired.peers[0].State.Word != "Paper lantern", "selection timeout always falls back to an ordinary offered answer");

        var aliases = Room(); var drawer = aliases.peers[0];
        aliases.game.Handle(drawer, new("choose-word", Text: "Aeroplane", TurnId: drawer.State.TurnId));
        aliases.game.Handle(aliases.peers[1], new("chat", Text: "AIRPLANE!"));
        check(aliases.peers[1].State.Players.Single(p => p.Profile.Id == aliases.peers[1].Id) is { Solved: true, Score: 1000 }, "curated alternate spellings solve with normal scores");
        aliases.game.Handle(aliases.peers[2], new("chat", Text: "Aeroplanes"));
        check(aliases.peers[0].State.Phase == "intermission", "curated plural aliases complete the turn without room echo");
        string metadata = Encoding.UTF8.GetString(Protocol.Encode(new ServerEvent("wordlists", WordLists: bank.Catalog)));
        check(metadata.Contains("General") && !metadata.Contains("Airplane") && !metadata.Contains("Aeroplane") && !metadata.Contains("aliases"), "category catalog never publishes answers or their accepted aliases");
        check(bank.Answers("Moogle", ["life"]).SequenceEqual(new[] { "MOOGLE" }), "aliases outside a room's selected lists cannot affect its answer matching");
        bool invalidAlias = false;
        try { _ = new WordBank([new("bad", "Bad aliases", true, false, ["Cat", "Dog"], Aliases: new() { ["Not an answer"] = ["Bird"] })]); }
        catch (InvalidDataException) { invalidAlias = true; }
        check(invalidAlias, "orphan answer aliases fail private-bank validation");

        check(aliases.peers.All(p => !p.Events.Any(e => e.Chat?.Text is "A new drawing turn has started." or "Turn finished. The answer is shown above the canvas.")), "redundant start and turn-finished server messages are absent");
        check(aliases.peers[0].Events.Any(e => e.Chat?.Text.Contains("is choosing the next word") == true), "the named choosing announcement is retained");
        // Finish the three-person match; preserve the departed first solver.
        aliases.game.Handle(aliases.peers[1], new("leave"));
        for (int i = 0; i < 6 && drawer.State.Phase != "results"; i++)
        {
            aliases.clock.Advance(100); aliases.game.Tick();
        }
        var final = aliases.peers[0].State;
        check(final.Phase == "results" && final.MatchResults is { Length: 3 } && final.MatchResults[0].PlayerId == aliases.peers[1].Id && final.MatchResults[0].Points == 1000,
            "final standings retain departed players and authoritative total scores");
        check(final.MatchResults!.Select(s => s.Points).SequenceEqual(final.MatchResults.Select(s => s.Points).OrderDescending()), "match podium standings are ordered by total score");
        aliases.game.Handle(drawer, new("configure", Options: final.Options with { CustomWordsEnabled = false, WordLists = ["xiv"], WordChoiceCount = 2 }));
        check(drawer.State.Options is { CustomWordsEnabled: false, WordChoiceCount: 2 } && drawer.State.Options.WordLists!.SequenceEqual(new[] { "xiv" }), "leader can change categories and rare-word settings between matches");
        aliases.game.Handle(aliases.peers[2], new("configure", Options: final.Options));
        check(aliases.peers[2].Events.Last().Type == "error", "non-leaders cannot change room rules");
        var active = Room(); active.game.Handle(active.peers[0], new("configure", Options: new("Cannot change during match")));
        check(active.peers[0].Events.Last().Type == "error", "room rules cannot change during active selection or drawing");
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
