using System.Net.WebSockets;
using System.Text.Json;
using Kupopop;
using Kupopop.Server;
using Kupopop.Shared;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ReviewPreparationChecks
{
    public static async Task RunAsync(string working, Action<bool, string> check)
    {
        await CheckTransportAsync(check);
        CheckStatistics(working, check);
        CheckSessionExpiry(working, check);
        CheckBans(working, check);
        CheckStarterAndCache(working, check);
        await CheckDiagnosticsAsync(working, check);
        CheckAllocationsAndRetry(check);
    }

    private static async Task CheckTransportAsync(Action<bool, string> check)
    {
        var portraits = Enumerable.Range(0, 130).Select(i => new PlayerProfile(Guid.NewGuid(),
            "Artist " + i.ToString("D3") + new string('x', 30), i, new byte[4096])).ToArray();
        var social = new ServerEvent("social", Social: new(
            portraits.Select((p, i) => new SocialPerson(p, i < 100 ? "friend" : "recent")).ToArray(),
            new(true, true, true), portraits.Take(20).Select(p => new GameInvitation(Guid.NewGuid(), p,
                new string('r', 40), DateTimeOffset.UtcNow.AddMinutes(5))).ToArray()));
        var state = new ServerEvent("state", Room: new("12345678", new("Large room"), "waiting",
            portraits[0].Id, null, Guid.Empty, 0, default, "", null, false,
            portraits.Take(16).Select((p, i) => new PlayerView(p, 0, true, false, i == 0, false)).ToArray(),
            DateTimeOffset.UtcNow, new byte[Protocol.CanvasWidth * Protocol.CanvasHeight]));
        using var socket = new SocketProbe();
        var peer = new Kupopop.Server.Peer(portraits[0].Id, socket);
        var startup = new[] { new ServerEvent("profile", Profile: portraits[0]),
            new("wordlists", WordLists: WordBank.Starter().Catalog), state, social };
        int bytes = startup.Sum(e => Protocol.Encode(e).Length);
        check(startup.All(Protocol.ValidEvent) && bytes > 1024 * 1024 && bytes < TransportLimits.ServerQueueBytes,
            "maximum legitimate startup batch exceeds the old budget and fits the new bounded budget");
        foreach (var message in startup)
        {
            peer.Send(message);
        }

        check(socket.State == WebSocketState.Open, "large startup state does not abort before the send pump starts");
        peer.Complete();
        await peer.PumpAsync(CancellationToken.None);
        check(socket.Sent.Count == startup.Length && socket.Sent.Zip(startup)
            .All(pair => pair.First.SequenceEqual(Protocol.Encode(pair.Second))), "startup messages drain unchanged in order");

        using var slow = new SocketProbe();
        var congested = new Kupopop.Server.Peer(Guid.NewGuid(), slow);
        for (int i = 0; i < 4; i++)
        {
            congested.Send(social);
        }

        check(slow.State == WebSocketState.Aborted, "slow consumers still cannot exceed the finite outbound byte budget");

        foreach (bool fragment in new[] { false, true })
        {
            using var stalled = new SocketProbe(stall: true, fragment: fragment);
            bool expired = false;
            try
            {
                await Protocol.ReceiveAsync(stalled, TransportLimits.ServerMessageBytes,
                CancellationToken.None, TimeSpan.FromMilliseconds(30));
            }
            catch (OperationCanceledException) { expired = true; }
            check(expired, fragment ? "client receive deadline covers an incomplete trickled message"
                : "client receive deadline expires an open but silent application connection");
        }
    }

    private static void CheckStatistics(string working, Action<bool, string> check)
    {
        using var store = new AccountStore(Path.Combine(working, "privacy-eligibility"));
        for (int scenario = 0; scenario < 5; scenario++)
        {
            var clock = new Clock();
            var game = new GameEngine(store, NullLogger<GameEngine>.Instance, clock, customWordRoll: () => 99);
            var a = new TestPeer(store.Create("Privacy A " + scenario, trackStats: true).AccountId);
            var b = new TestPeer(store.Create("Privacy B " + scenario, trackStats: true).AccountId);
            game.Attach(a);
            game.Attach(b);
            game.Handle(a, new("create", Options: new("Privacy room", Rounds: 1, TurnSeconds: 15)));
            game.Handle(b, new("join", Text: a.State.Code));
            game.Handle(a, new("start"));
            var command = scenario switch
            {
                0 => ClientCommands.ProfilePrivacy(true, true),
                1 => ClientCommands.ProfilePrivacy(true, false, backgroundInvitations: true),
                2 => ClientCommands.ProfilePrivacy(false, false),
                3 => ClientCommands.ProfilePrivacy(true, false, resetStats: true),
                _ => ClientCommands.ProfilePrivacy(false, false),
            };
            game.Handle(a, command);
            if (scenario == 4)
            {
                game.Handle(a, ClientCommands.ProfilePrivacy(true, false));
            }

            for (int turn = 0; turn < 2; turn++)
            {
                var drawer = a.State.DrawerId == a.Id ? a : b;
                game.Handle(drawer, new("choose-word", Text: drawer.State.WordChoices![0], TurnId: drawer.State.TurnId));
                clock.Advance(16);
                game.Tick();
                clock.Advance(7);
                game.Tick();
            }
            long games = store.Statistics(a.Id)?.Games ?? 0;
            check(a.State.Phase == "results" && games == (scenario < 2 ? 1 : 0) && store.Statistics(b.Id)!.Games == 1,
                "match eligibility respects independent privacy changes, opt-out and reset, scenario " + scenario);
        }
    }

    private static void CheckSessionExpiry(string working, Action<bool, string> check)
    {
        using var store = new AccountStore(Path.Combine(working, "session-expiry"));
        var clock = new Clock();
        var game = new GameEngine(store, NullLogger<GameEngine>.Instance, clock);
        var a = new TestPeer(store.Create("Session A").AccountId);
        var b = new TestPeer(store.Create("Session B").AccountId);
        game.Attach(a);
        game.Attach(b);
        game.Handle(a, new("create", Options: new("Session room")));
        game.Handle(b, new("join", Text: a.State.Code));
        game.Handle(b, new("leave"));
        game.Detach(b);
        clock.Advance(10);
        game.Tick();
        var early = new TestPeer(b.Id);
        game.Attach(early);
        check(early.Social.People.Any(p => p.Relationship == "recent"), "short menu reconnect preserves the current session's recents");
        game.Detach(early);
        clock.Advance(31);
        game.Tick();
        var late = new TestPeer(b.Id);
        game.Attach(late);
        check(late.Social.People.Length == 0, "menu disconnect expires recents independently of room membership");
        game.Handle(late, new("view-profile", TargetId: a.Id));
        check(late.Events.Last().Type == "error", "expired encounters no longer authorize profile lookup");
        game.Handle(late, new("join", Text: a.State.Code));
        game.Detach(late);
        clock.Advance(31);
        var afterGrace = new TestPeer(b.Id);
        game.Attach(afterGrace);
        check(afterGrace.RoomCode is null && afterGrace.Social.People.Length == 0,
            "late attach cannot restore an expired reservation even before the next engine tick");
    }

    private static void CheckBans(string working, Action<bool, string> check)
    {
        using var store = new AccountStore(Path.Combine(working, "room-bans"));
        var clock = new Clock();
        var game = new GameEngine(store, NullLogger<GameEngine>.Instance, clock);
        var a = new TestPeer(store.Create("Ban A").AccountId);
        var b = new TestPeer(store.Create("Ban B").AccountId);
        var c = new TestPeer(store.Create("Ban C").AccountId);
        foreach (var peer in new[] { a, b, c })
        {
            game.Attach(peer);
        }

        game.Handle(a, new("create", Options: new("Ban room", IsPublic: true, Rounds: 1, TurnSeconds: 15)));
        string code = a.State.Code;
        game.Handle(b, new("join", Text: code));
        game.Handle(c, new("join", Text: code));
        game.Handle(a, new("friend-request", TargetId: b.Id));
        game.Handle(b, new("friend-accept", TargetId: a.Id));
        game.Handle(b, new("leave"));
        game.Handle(a, new("invite", TargetId: b.Id));
        var invite = b.Social.Invitations.Single();
        game.Handle(b, new("join", Text: code));
        game.Handle(a, new("kick", Text: b.Id.ToString()));
        game.Handle(b, new("join", Text: code));
        check(b.RoomCode is null && b.Events.Last().Type == "error", "host kick bans rejoining by public room code");
        game.Handle(b, new("accept-invite", TargetId: invite.Id));
        check(b.RoomCode is null && b.Events.Last().Type == "error",
            "an old invitation cannot bypass a room ban");
        game.Handle(a, new("invite", TargetId: b.Id));
        check(a.Events.Last().Type == "error", "new invitations cannot target an account banned from this room");
        game.Detach(b);
        var reconnected = new TestPeer(b.Id);
        game.Attach(reconnected);
        game.Handle(reconnected, new("join", Text: code));
        check(reconnected.RoomCode is null, "room bans survive reconnecting the same account");
        game.Handle(a, new("start"));
        for (int turn = 0; turn < 2; turn++)
        {
            var drawer = a.State.DrawerId == a.Id ? a : c;
            game.Handle(drawer, new("choose-word", Text: drawer.State.WordChoices![0], TurnId: drawer.State.TurnId));
            clock.Advance(16);
            game.Tick();
            clock.Advance(7);
            game.Tick();
        }
        game.Handle(reconnected, new("join", Text: code));
        check(a.State.Phase == "results" && reconnected.RoomCode is null,
            "room bans remain in force after the match ends");
        game.Handle(a, new("leave"));
        game.Handle(c, new("kick", Text: a.Id.ToString())); // Missing member is rejected, room survives.
        game.Handle(a, new("create", Options: new("Different room")));
        game.Handle(reconnected, new("join", Text: a.State.Code));
        check(reconnected.RoomCode == a.State.Code, "a ban is scoped to the original room, not the whole service");

        // Transfer leadership before starting so the first drawer can be voted out.
        var vote = new GameEngine(store, NullLogger<GameEngine>.Instance, clock);
        foreach (var peer in new[] { a, b, c })
        {
            peer.RoomCode = null;
            vote.Attach(peer);
        }
        vote.Handle(a, new("create", Options: new("Vote ban room")));
        string voteCode = a.State.Code;
        vote.Handle(b, new("join", Text: voteCode));
        vote.Handle(c, new("join", Text: voteCode));
        vote.Detach(a);
        vote.Attach(a);
        vote.Handle(b, new("start"));
        vote.Handle(a, new("choose-word", Text: a.State.WordChoices![0], TurnId: a.State.TurnId));
        vote.Handle(b, new("vote-kick", TurnId: a.State.TurnId));
        vote.Handle(c, new("vote-kick", TurnId: a.State.TurnId));
        vote.Handle(a, new("join", Text: voteCode));
        check(a.RoomCode is null && a.Events.Last().Type == "error", "successful vote-kick also bans the drawer from the room");
    }

    private static void CheckStarterAndCache(string working, Action<bool, string> check)
    {
        string bankPath = Path.Combine(working, "fresh-starter");
        check(WordBank.Open(bankPath).Choices(null, "", 6).Length == 6 && WordBank.Open(bankPath).Choices(null, "", 8).Length == 8,
            "fresh starter supports the six-choice default and eight-choice maximum");
        File.WriteAllBytes(Path.Combine(bankPath, "word-lists.json"), JsonSerializer.SerializeToUtf8Bytes(new[] {
            new ServerWordList("starter", "FFXIV Starter", true, false, ["Moogle", "Chocobo", "Cactuar", "Spriggan"]) }, Protocol.Json));
        check(WordBank.Open(bankPath).Catalog.Single().Count == 8, "untouched four-word starter also upgrades safely");
        using var store = new AccountStore(Path.Combine(working, "profile-cache"));
        var account = store.Create("Cache artist");
        store.Update(account.AccountId, null, new byte[4096], false);
        var old = store.Get(account.AccountId)!;
        old.AvatarPixels![0] = 9;
        check(store.Get(account.AccountId)!.AvatarPixels![0] == 0, "profile cache does not expose its mutable pixel arrays");
        store.Update(account.AccountId, "Updated cache artist", null, true);
        check(store.Get(account.AccountId) is { DisplayName: "Updated cache artist", AvatarPixels: null },
            "committed profile changes invalidate cached names and portraits together");
        var batch = store.SocialProfiles([account.AccountId]);
        check(batch[account.AccountId].Profile.DisplayName == "Updated cache artist" && !batch[account.AccountId].Preferences.TrackStats,
            "batched social profiles preserve current privacy settings");
        using var other = new AccountStore(Path.Combine(working, "profile-cache"));
        other.Update(account.AccountId, "External cache edit", null, true);
        check(store.Get(account.AccountId)!.DisplayName == "External cache edit", "profile cache observes commits from another SQLite connection");
    }

    private static async Task CheckDiagnosticsAsync(string working, Action<bool, string> check)
    {
        using var log = new DiagnosticLog(Path.Combine(working, "diagnostics"));
        log.Write("routine trace");
        log.Write("operation_failed", new InvalidOperationException("private input must not be logged"));
        log.Verbose = true;
        log.Write("explicit debug trace");
        log.Dispose();
        await log.Completion;
        string text = File.ReadAllText(log.FilePath);
        check(!text.Contains("routine trace") && !text.Contains("private input") && text.Contains("operation_failed") && text.Contains("explicit debug trace"),
            "background diagnostics suppress routine tracing and exception payloads while retaining failures and explicit debug output");
    }

    private static void CheckAllocationsAndRetry(Action<bool, string> check)
    {
        var canvas = new PixelCanvas();
        var buffer = new byte[canvas.Pixels.Length * 4];
        canvas.WriteRgba(buffer);
        check(buffer.SequenceEqual(canvas.Rgba()), "reusable canvas conversion preserves every RGBA pixel");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            canvas.WriteRgba(buffer);
        }

        check(GC.GetAllocatedBytesForCurrentThread() - before < 1024, "repeated canvas staging avoids large per-frame managed allocations");
        var low = new ConnectionRetry(_ => 0);
        var high = new ConnectionRetry(max => max - 1);
        low.Failed(0);
        high.Failed(0);
        check(high.NextAttempt < low.NextAttempt && high.NextAttempt >= 1500, "reconnect jitter spreads attempts within a bounded safe interval");
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(int seconds) => now = now.AddSeconds(seconds);
    }

    private sealed class TestPeer(Guid id) : IGamePeer
    {
        public Guid Id { get; } = id;
        public string? RoomCode
        {
            get; set;
        }
        public List<ServerEvent> Events { get; } = [];
        public RoomView State => Events.Last(e => e.Room is not null).Room!;
        public SocialView Social => Events.Last(e => e.Social is not null).Social!;
        public void Send(ServerEvent message) => Events.Add(message);
        public bool Allow(string type) => true;
        public void Close()
        {
        }
    }

    private sealed class SocketProbe(bool stall = false, bool fragment = false) : WebSocket
    {
        private bool aborted, fragmentSent;
        public List<byte[]> Sent { get; } = [];
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => aborted ? WebSocketState.Aborted : WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() => aborted = true;
        public override void Dispose()
        {
        }
        public override Task CloseAsync(WebSocketCloseStatus status, string? text, CancellationToken cancel) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? text, CancellationToken cancel) => Task.CompletedTask;
        public override Task SendAsync(ArraySegment<byte> data, WebSocketMessageType type, bool end, CancellationToken cancel)
        {
            Sent.Add(data.ToArray());
            return Task.CompletedTask;
        }
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancel)
        {
            if (fragment && !fragmentSent)
            {
                fragmentSent = true;
                buffer[0] = (byte)'{';
                return new(1, WebSocketMessageType.Text, false);
            }
            if (stall)
            {
                await Task.Delay(Timeout.Infinite, cancel);
            }

            throw new InvalidOperationException("Receive not expected.");
        }
    }
}
