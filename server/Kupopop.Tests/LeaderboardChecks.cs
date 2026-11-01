using Kupopop.Server;
using Kupopop.Shared;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

static class LeaderboardChecks
{
    public static void Run(string working, Action<bool, string> check)
    {
        string root = Path.Combine(working, "leaderboards");
        using var store = new AccountStore(root);
        var a = store.Create("Named only by choice", trackStats: true);
        var b = store.Create("Another private artist", trackStats: true);
        var privateAccount = store.Create("No statistics");
        byte[] custom = AvatarGenerator.Generate(321);
        store.Update(a.AccountId, null, custom, false);
        store.RecordMatch([new(a.AccountId, a.Profile.DisplayName, 100), new(b.AccountId, b.Profile.DisplayName, 200)], [a.AccountId, b.AccountId]);
        store.RecordMatch([new(a.AccountId, a.Profile.DisplayName, 150)], [a.AccountId]);
        store.RecordDrawingLike(b.AccountId); store.RecordDrawingLike(privateAccount.AccountId);
        var board = store.Leaderboard(a.AccountId, "best", 0);
        check(board.Entries.Length == 2 && board.Entries.All(e => e.Name == "Anonymous") && !board.Named && !board.Hidden && board.ShowPortrait,
            "tracked accounts appear anonymously by default; non-consenting statistics accounts are excluded");
        check(board.Entries[0].BestScore == 200 && board.Entries[1].AvatarPixels!.SequenceEqual(custom),
            "best-game ranking uses authoritative totals and preserves custom portraits");
        check(store.Leaderboard(a.AccountId, "points", 0).Entries[0].Points == 250 &&
            store.Leaderboard(a.AccountId, "games", 0).Entries[0].Games == 2 &&
            store.Leaderboard(a.AccountId, "likes", 0).Entries[0].Likes == 1,
            "four leaderboard metrics sort independently");
        store.SetLeaderboardPreference(a.AccountId, "name", true);
        check(store.Leaderboard(a.AccountId, "points", 0).Entries[0].Name == a.Profile.DisplayName,
            "only an explicit name preference publishes a display name");
        store.SetLeaderboardPreference(a.AccountId, "portrait", false);
        check(store.Leaderboard(a.AccountId, "points", 0).Entries[0] is { AvatarPixels: null, AvatarPreset: null, AvatarSeed: 123 },
            "portrait opt-out removes custom pixels, preset and original seed from leaderboard packets");
        store.SetLeaderboardPreference(a.AccountId, "hidden", true);
        check(store.Leaderboard(a.AccountId, "best", 0).Entries.Length == 1 && store.Statistics(a.AccountId)?.Games == 2,
            "leaderboard opt-out takes effect immediately without deleting private statistics");
        store.SetLeaderboardPreference(a.AccountId, "hidden", false);
        store.SetPreferences(b.AccountId, false, false, false);
        check(store.Leaderboard(a.AccountId, "likes", 0).Entries.Length == 1,
            "disabling statistics excludes the player from every leaderboard");
        store.SetPreferences(b.AccountId, true, false, false);
        check(store.Leaderboard(b.AccountId, "likes", 0).Entries.All(e => e.Likes == 0),
            "statistics opt-out erases persistent likes as well as game totals");
        bool rejected = false;
        try { store.Leaderboard(a.AccountId, "best; DROP TABLE accounts", 0); } catch (ArgumentException) { rejected = true; }
        check(rejected, "leaderboard sorting rejects arbitrary SQL and unknown metrics");
        rejected = false;
        try { store.Leaderboard(a.AccountId, "best", -1); } catch (ArgumentException) { rejected = true; }
        check(rejected, "leaderboard page bounds are server-enforced");
        for (int i = 0; i < 51; i++) store.Create("Paged artist " + i, trackStats: true);
        check(store.Leaderboard(a.AccountId, "best", 0) is { Entries.Length: 50, HasNext: true } &&
            store.Leaderboard(a.AccountId, "best", 1) is { Entries.Length: 3, HasNext: false },
            "leaderboard replies are bounded to fifty rows with stable pagination");
        var encoded = Protocol.Encode(new ServerEvent("leaderboard", Leaderboard: store.Leaderboard(a.AccountId, "best", 0)));
        check(Protocol.ValidEvent(new("leaderboard", Leaderboard: board)) && !System.Text.Encoding.UTF8.GetString(encoded).Contains(a.AccountId.ToString()),
            "valid leaderboard packets expose no account identifiers or private relationships");

        var clock = new Clock();
        var engine = new GameEngine(store, NullLogger<GameEngine>.Instance, clock);
        var drawer = new Peer(a.AccountId); var viewer = new Peer(b.AccountId);
        engine.Attach(drawer); engine.Attach(viewer);
        engine.Handle(drawer, new("create", Options: new("Like checks", Rounds: 1)));
        engine.Handle(viewer, new("join", Text: drawer.State.Code)); engine.Handle(drawer, new("start"));
        Guid turn = drawer.State.TurnId;
        engine.Handle(viewer, new("like-drawing", TurnId: turn));
        check(viewer.Events[^1].Type == "error", "choosing-phase likes are rejected");
        engine.Handle(drawer, new("choose-word", Text: drawer.State.WordChoices![0], TurnId: turn));
        engine.Handle(drawer, new("like-drawing", TurnId: turn));
        check(drawer.Events[^1].Type == "error", "artists cannot like their own drawings");
        engine.Handle(viewer, new("like-drawing", TurnId: Guid.NewGuid()));
        check(viewer.Events[^1].Type == "error", "stale-turn likes cannot affect a current drawing");
        engine.Handle(viewer, new("like-drawing", TurnId: turn));
        engine.Handle(viewer, new("like-drawing", TurnId: turn));
        check(viewer.State is { DrawingLikes: 1, YouLikedDrawing: true } && drawer.State is { DrawingLikes: 1, YouLikedDrawing: false },
            "one accepted drawing like is authoritative, broadcast and duplicate-proof");
        engine.Detach(viewer); engine.Attach(viewer); engine.Handle(viewer, new("like-drawing", TurnId: turn));
        check(viewer.State.DrawingLikes == 1, "reconnecting cannot grant an additional like for the same drawing");
        check(store.Leaderboard(a.AccountId, "likes", 0).Entries.Single(e => e.Name == a.Profile.DisplayName).Likes == 1 &&
            viewer.State.Players.All(p => p.Score == 0), "one persistent like never changes match scores or credits duplicate packets");
        clock.Advance(91); engine.Tick(); engine.Handle(viewer, new("like-drawing", TurnId: turn));
        check(viewer.Events[^1].Type == "error", "likes after the deadline are rejected");
        clock.Advance(7); engine.Tick();
        check(viewer.State.DrawingLikes == 0 && !viewer.State.YouLikedDrawing, "new drawings clear turn-scoped like state");

        using (var raw = new SqliteConnection("Data Source=" + Path.Combine(root, "accounts.db")))
        {
            raw.Open(); using var legacy = raw.CreateCommand();
            legacy.CommandText = "UPDATE accounts SET avatar_preset='job-paladin' WHERE id=$id OR id=$other; PRAGMA user_version=4";
            legacy.Parameters.AddWithValue("$id", a.AccountId.ToString()); legacy.Parameters.AddWithValue("$other", b.AccountId.ToString()); legacy.ExecuteNonQuery();
        }
        using var upgraded = new AccountStore(root);
        check(upgraded.Get(a.AccountId) is { AvatarPreset: null } && upgraded.Get(a.AccountId)!.AvatarPixels!.SequenceEqual(custom) &&
            upgraded.Authenticate(a.AccessToken)?.Id == a.AccountId,
            "job icon migration resets the preset while preserving custom pixels and credentials");
        check(upgraded.Get(b.AccountId) is { AvatarPreset: null, AvatarPixels: null } &&
            upgraded.Get(b.AccountId)!.AvatarSeed == b.Profile.AvatarSeed,
            "legacy job-only portraits become the original account's default moogle");
        check(upgraded.Leaderboard(a.AccountId, "points", 0).Named && !upgraded.Leaderboard(a.AccountId, "points", 0).ShowPortrait,
            "leaderboard visibility choices survive restart and migration");
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(int seconds) => now = now.AddSeconds(seconds);
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
