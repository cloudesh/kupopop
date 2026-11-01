using Kupopop.Server;
using Kupopop.Shared;
using Microsoft.Extensions.Logging.Abstractions;

static class LateJoinChecks
{
    public static void Run(string working, Action<bool, string> check)
    {
        using var accounts = new AccountStore(Path.Combine(working, "late-joining"));
        var clock = new Clock();
        var game = new GameEngine(accounts, NullLogger<GameEngine>.Instance, clock);
        var peers = Enumerable.Range(0, 8).Select(i => new Peer(accounts.Create("Late join artist " + i).AccountId)).ToArray();
        foreach (var peer in peers) game.Attach(peer);
        var host = peers[0]; var original = peers[1]; var newcomer = peers[2]; var browser = peers[7];
        game.Handle(host, new("create", Options: new("Public live room", IsPublic: true, Rounds: 2, MaxPlayers: 5, TurnSeconds: 15)));
        string code = host.State.Code;
        game.Handle(original, new("join", Text: code));
        game.Handle(host, new("start"));
        var firstTurn = host.State.TurnId;
        game.Handle(browser, new("list"));
        check(browser.Events.Last(e => e.Rooms is not null).Rooms!.Single(r => r.Code == code).Phase == "choosing",
            "public room remains listed while its artist is choosing a word");
        game.Handle(newcomer, new("join", Text: code));
        check(newcomer.State.Phase == "choosing" && newcomer.State.WordChoices is null && newcomer.State.Word is null &&
            newcomer.State.Canvas is { Length: Protocol.CanvasWidth * Protocol.CanvasHeight } && host.State.TurnId == firstTurn && host.State.Round == 1,
            "late join during selection sends a private-safe snapshot without restarting the turn or round");
        game.Handle(host, new("choose-word", Text: host.State.WordChoices![0], TurnId: firstTurn));
        game.Handle(host, new("draw", TurnId: firstTurn, Draw: new("pencil", 10, 10, 20, 20, 1, 2)));
        game.Handle(browser, new("list"));
        check(browser.Events.Last(e => e.Rooms is not null).Rooms!.Single(r => r.Code == code).Phase == "drawing",
            "public room remains discoverable during drawing");
        game.Handle(peers[3], new("join", Text: code));
        check(peers[3].State.Word is null && peers[3].State.WordChoices is null && peers[3].State.Canvas!.Any(p => p == 1),
            "mid-drawing join receives the current picture and hints without answers or unused choices");
        game.Handle(host, new("chat", Text: "Private done-mode message"));
        check(!newcomer.Events.Any(e => e.Chat?.Text == "Private done-mode message"), "late guessers cannot read done-mode messages");
        game.Handle(newcomer, new("chat", Text: host.State.Word));
        check(newcomer.State.Players.Single(p => p.Profile.Id == newcomer.Id) is { Score: 1000, Solved: true },
            "newcomers may solve immediately and receive the authoritative solve-order award");
        game.Handle(newcomer, new("leave"));
        game.Handle(newcomer, new("join", Text: code));
        int before = host.State.Players.Single(p => p.Profile.Id == host.Id).Score;
        game.Handle(newcomer, new("chat", Text: host.State.Word));
        check(newcomer.State.YouAreDone && newcomer.State.Players.Single(p => p.Profile.Id == newcomer.Id).Score == 1000 &&
            host.State.Players.Single(p => p.Profile.Id == host.Id).Score == before,
            "leave/rejoin preserves score and known-word status without duplicate guess or drawer awards");
        game.Handle(newcomer, new("like-drawing", TurnId: firstTurn));
        int likes = host.State.DrawingLikes;
        game.Handle(newcomer, new("leave")); game.Handle(newcomer, new("join", Text: code));
        game.Handle(newcomer, new("like-drawing", TurnId: firstTurn));
        check(host.State.DrawingLikes == likes, "late rejoining cannot like the same drawing twice");
        game.Handle(original, new("vote-skip", TurnId: firstTurn));
        game.Handle(peers[4], new("join", Text: code));
        game.Handle(peers[4], new("vote-skip", TurnId: firstTurn));
        check(peers[4].Events.Last().Type == "error" && host.State.SkipVote is { Required: 3, Votes: 1 },
            "joining after a ballot starts does not change its eligible voters or threshold");
        game.Handle(peers[5], new("join", Text: code));
        check(peers[5].RoomCode is null && peers[5].Events.Last().Error == "Room is full.",
            "active rooms still enforce their configured capacity");
        game.Handle(peers[4], new("leave"));
        clock.Advance(16); game.Tick();
        game.Handle(peers[4], new("join", Text: code));
        check(peers[4].State.Phase == "intermission" && peers[4].State.Word == host.State.Word,
            "late join during the reveal receives the normal public answer and recap");
        game.Handle(peers[4], new("leave"));
        game.Handle(peers[3], new("leave"));
        clock.Advance(7); game.Tick();
        check(host.State.Round == 1 && host.State.DrawerId == original.Id,
            "late joins do not extend the frozen current-round drawing roster");
        ChooseAndFinish(original);
        check(host.State.Round == 2 && host.State.DrawerId == host.Id, "the next round keeps its declared number after a late join");
        var secondRound = new List<Guid>();
        while (host.State.Phase != "results")
        {
            var drawer = peers.Single(p => p.Id == host.State.DrawerId);
            secondRound.Add(drawer.Id); ChooseAndFinish(drawer);
            if (secondRound.Count > 5) throw new Exception("Late joining extended the round indefinitely.");
        }
        check(secondRound.SequenceEqual(new[] { host.Id, original.Id, newcomer.Id }),
            "a newcomer receives exactly one drawing turn in each subsequent round");
        check(host.State.MatchResults!.Any(r => r.PlayerId == newcomer.Id && r.Points == 1000),
            "late entrants appear in the final standings with their earned score");
        check(peers.SelectMany(p => p.Events).All(Protocol.ValidEvent), "late-join snapshots and standings remain valid for the bounded protocol");

        // Existing private code/invitation access still works once a match starts.
        game.Handle(host, new("friend-request", TargetId: newcomer.Id));
        game.Handle(newcomer, new("friend-accept", TargetId: host.Id));
        game.Handle(newcomer, new("leave"));
        game.Handle(host, new("configure", Options: host.State.Options with { IsPublic = false, Rounds = 1 }));
        game.Handle(host, new("invite", TargetId: newcomer.Id));
        var invite = newcomer.Events.Last(e => e.Social is not null).Social!.Invitations.Single();
        game.Handle(host, new("start"));
        game.Handle(newcomer, new("accept-invite", TargetId: invite.Id));
        check(newcomer.RoomCode == code && newcomer.State.Phase == "choosing",
            "an invitation sent before starting remains valid during the match");
        game.Handle(browser, new("list"));
        check(browser.Events.Last(e => e.Rooms is not null).Rooms!.All(r => r.Code != code),
            "private rooms remain hidden while in progress");
        game.Handle(newcomer, new("leave")); clock.Advance(4);
        game.Handle(host, new("invite", TargetId: newcomer.Id));
        invite = newcomer.Events.Last(e => e.Social is not null).Social!.Invitations.Single();
        game.Handle(newcomer, new("accept-invite", TargetId: invite.Id));
        check(newcomer.RoomCode == code, "hosts may invite accepted friends while their room is playing");
        game.Detach(newcomer);
        game.Handle(browser, new("list"));
        var replacement = new Peer(newcomer.Id); game.Attach(replacement);
        check(replacement.State.Phase == "choosing" && replacement.State.DrawerId == host.Id,
            "late-join reconnection restores its room without a second roster entry");
        void ChooseAndFinish(Peer drawer)
        {
            game.Handle(drawer, new("choose-word", Text: drawer.State.WordChoices![0], TurnId: drawer.State.TurnId));
            clock.Advance(16); game.Tick(); clock.Advance(7); game.Tick();
        }

        // The protocol and match-result bounds remain 16, including departed
        // participants, so repeatedly swapping accounts cannot grow a match.
        var bounded = new GameEngine(accounts, NullLogger<GameEngine>.Instance, clock);
        var members = Enumerable.Range(0, 17).Select(i => new Peer(accounts.Create("Bounded join artist " + i).AccountId)).ToArray();
        foreach (var peer in members) bounded.Attach(peer);
        bounded.Handle(members[0], new("create", Options: new("Bounded room", IsPublic: true)));
        string boundedCode = members[0].State.Code;
        bounded.Handle(members[1], new("join", Text: boundedCode)); bounded.Handle(members[0], new("start"));
        for (int i = 2; i < 16; i++) { bounded.Handle(members[i], new("join", Text: boundedCode)); bounded.Handle(members[i], new("leave")); }
        bounded.Handle(members[16], new("join", Text: boundedCode));
        check(members[16].RoomCode is null && members[16].Events.Last().Type == "error",
            "account churn cannot grow a match beyond sixteen distinct score and known-word records");
        bounded.Handle(members[2], new("join", Text: boundedCode));
        check(members[2].RoomCode == boundedCode, "existing match participants can still return at the distinct-player bound");
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
        public List<ServerEvent> Events { get; } = [];
        public RoomView State => Events.Last(e => e.Room is not null).Room!;
        public void Send(ServerEvent message) => Events.Add(message);
        public bool Allow(string type) => true;
        public void Close() { }
    }
}
