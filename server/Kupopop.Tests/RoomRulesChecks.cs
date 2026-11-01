using Kupopop.Server;
using Kupopop.Shared;
using Microsoft.Extensions.Logging.Abstractions;

static class RoomRulesChecks
{
    public static void Run(string working, Action<bool, string> check)
    {
        var accounts = new AccountStore(Path.Combine(working, "room-rules"));
        var ids = Enumerable.Range(0, 3).Select(i => accounts.Create("Rules artist " + i)).ToArray();
        var bank = new WordBank([new("rules", "Rules test", true, false,
            ["Moogle", "Chocobo", "Cactuar", "Spriggan", "Alpha lantern", "Bravo needle", "Charlie pencil", "Delta canvas", "Echo ribbon"])]);
        (GameEngine game, Peer[] peers) Room(RoomOptions options, bool start = true)
        {
            var game = new GameEngine(accounts, NullLogger<GameEngine>.Instance, TimeProvider.System, bank);
            var peers = ids.Select(c => new Peer(c.AccountId)).ToArray();
            foreach (var peer in peers) game.Attach(peer);
            game.Handle(peers[0], new("create", Options: options));
            for (int i = 1; i < peers.Length; i++) game.Handle(peers[i], new("join", Text: peers[0].State.Code));
            if (start) game.Handle(peers[0], new("start"));
            return (game, peers);
        }
        foreach (int count in new[] { 1, 3, 8 })
        {
            var test = Room(new("Choice rules", WordChoiceCount: count));
            var state = test.peers[0].State;
            check(state.WordChoices?.Length == count && state.WordChoices.Distinct().Count() == count,
                $"configured {count} word offers are distinct and server-owned");
            check(test.peers.Skip(1).All(p => p.State.WordChoices is null && p.State.Word is null),
                $"configured {count} word offers remain private from spectators");
            test.game.Handle(test.peers[0], new("choose-word", Text: state.WordChoices![^1], TurnId: state.TurnId));
            check(test.peers[0].State is { Phase: "drawing" } drawing && drawing.Word == state.WordChoices[^1],
                $"drawer can select the last of {count} offers");
        }
        foreach (int invalid in new[] { 0, 9 })
        {
            var game = new GameEngine(accounts, NullLogger<GameEngine>.Instance, TimeProvider.System, bank);
            var peer = new Peer(ids[0].AccountId); game.Attach(peer);
            game.Handle(peer, new("create", Options: new("Invalid choices", WordChoiceCount: invalid)));
            check(peer.RoomCode is null && peer.Events.Last().Type == "error", "out-of-range word-choice count is rejected on the server");
        }
        bool smallRejected = false;
        try { WordBank.Starter().Choices(null, "", 8); } catch (ArgumentException) { smallRejected = true; }
        check(smallRejected, "insufficient word pools cannot silently reduce the requested offers");

        var disabled = Room(new("No voting", VoteKickEnabled: false, VoteSkipEnabled: false));
        var lead = disabled.peers[0];
        disabled.game.Handle(lead, new("choose-word", Text: lead.State.WordChoices![0], TurnId: lead.State.TurnId));
        check(disabled.peers.All(p => !p.State.KickVoteAvailable && !p.State.SkipVoteAvailable), "disabled room votes are unavailable in every client snapshot");
        foreach (string kind in new[] { "vote-kick", "vote-skip" })
        {
            foreach (var p in disabled.peers) p.Events.RemoveAll(e => e.Room is null);
            disabled.game.Handle(disabled.peers[1], new(kind, TurnId: lead.State.TurnId));
            check(disabled.peers[1].Events.Last().Type == "error" && disabled.peers.All(p => !p.Events.Any(e => e.Chat is not null)) && lead.State.Phase == "drawing",
                $"disabled {kind} cannot be bypassed by a direct network command");
        }
        disabled.game.Handle(lead, new("kick", Text: disabled.peers[2].Id.ToString()));
        check(disabled.peers[2].RoomCode is null, "room voting switches do not remove the leader's moderation action");
        foreach (bool kick in new[] { true, false })
        {
            var test = Room(new("Independent votes", VoteKickEnabled: kick, VoteSkipEnabled: !kick));
            check(test.peers[0].State.Options.VoteKickEnabled == kick && test.peers[0].State.Options.VoteSkipEnabled != kick &&
                test.peers[1].State.KickVoteAvailable == kick && test.peers[1].State.SkipVoteAvailable != kick,
                "kick and skip switches round-trip independently");
        }

        var near = Room(new("Near guesses", WordChoiceCount: 8));
        // Nine entries and eight offers: use a test-selected answer, then create
        // typo variants from it rather than assuming random offer contents.
        var chooser = near.peers[0]; string answer = chooser.State.WordChoices![0];
        near.game.Handle(chooser, new("choose-word", Text: answer, TurnId: chooser.State.TurnId));
        string normalized = GuessMatching.Normalize(answer);
        string substitute = (normalized[0] == 'Z' ? "X" : "Z") + normalized[1..];
        string transpose = normalized[1] + normalized[0].ToString() + normalized[2..];
        foreach (string typo in new[] { substitute, normalized + "Z", normalized[1..], transpose })
        {
            foreach (var p in near.peers) p.Events.RemoveAll(e => e.Room is null);
            near.game.Handle(near.peers[1], new("chat", Text: typo));
            check(near.peers[1].Events.Last().Chat is { System: true, Text: "So close! Try again." } &&
                near.peers[0].Events.All(e => e.Chat is null) && near.peers[2].Events.All(e => e.Chat is null) &&
                near.peers[1].State.Players.All(p => p.Score == 0),
                "one-edit guess produces private feedback without room echo, answer disclosure or scoring");
        }
        near.game.Handle(near.peers[1], new("chat", Text: "Just saying hello"));
        check(near.peers.All(p => p.Events.Last().Chat is { System: false, Text: "Just saying hello" }), "ordinary conversation still reaches the room");
        near.game.Handle(near.peers[1], new("chat", Text: answer.ToLowerInvariant() + "!"));
        check(near.peers[1].State.Players.Single(p => p.Profile.Id == near.peers[1].Id) is { Solved: true, Score: 1000 },
            "normalized exact answers still solve and score normally");
        foreach (var p in near.peers) p.Events.RemoveAll(e => e.Room is null);
        near.game.Handle(near.peers[0], new("chat", Text: substitute));
        check(near.peers[1].Events.Last().Chat is { Done: true, System: false } && near.peers[2].Events.All(e => e.Chat is null),
            "near-miss matching does not alter done-mode privacy or drawer conversation");
        check(!GuessMatching.IsNear("MOOGLE", "MOOGLE") && !GuessMatching.IsNear("MOXGLX", "MOOGLE") &&
            !GuessMatching.IsNear("HI", "IF"), "near matching excludes exact guesses, multiple edits and short conversational words");

        var square = new PixelCanvas();
        check(square.Width == 384 && square.Height == 384 && square.Apply(new("pencil", 383, 383, 383, 383, 4)) &&
            square.Pixels[^1] == 4 && !square.Apply(new("pencil", 384, 383, 384, 383, 4)),
            "square protocol canvas accepts its bottom-right pixel and rejects coordinates beyond it");
        foreach (float scale in new[] { 1f, 1.5f, 2f })
            foreach (var viewport in new[] { new System.Numerics.Vector2(752, 310), new System.Numerics.Vector2(912, 450), new System.Numerics.Vector2(1332, 600) })
            {
                var available = viewport * scale;
                var layout = Kupopop.UiGameLayout.Fit(available, scale);
                check(layout.Canvas > 0 && layout.Width + layout.Inset * 2 <= available.X + 0.01f && layout.Canvas + 10 * scale <= available.Y + 0.01f,
                    "square canvas and matching-height fixed side panels fit minimum/default/large room sizes at UI scale");
            }
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
