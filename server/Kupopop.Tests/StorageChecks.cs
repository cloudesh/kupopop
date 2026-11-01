using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.WebSockets;
using Kupopop.Server;
using Kupopop.Shared;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

static class StorageChecks
{
    public static void Run(string working, Action<bool, string> check)
    {
        string root = Path.Combine(working, "sqlite-migration");
        Directory.CreateDirectory(root);
        var id = Guid.NewGuid();
        const string token = "migration-access-secret-with-256-bit-test-fixture";
        const string recovery = "migration-recovery-secret-with-256-bit-test-fixture";
        var pixels = AvatarGenerator.Generate(733);
        var profile = new PlayerProfile(id, "Migration artist", 733, pixels);
        var legacy = new Dictionary<Guid, StoredAccount> { [id] = new(profile, Hash(token), Hash(recovery)) };
        string legacyPath = Path.Combine(root, "accounts.json");
        byte[] original = JsonSerializer.SerializeToUtf8Bytes(legacy, Protocol.Json);
        File.WriteAllBytes(legacyPath, original);
        using (var store = new AccountStore(root))
        {
            check(store.Count() == 1 && store.Authenticate(token)?.Id == id, "SQLite import retains identity and the existing access verifier");
            check(store.Get(id) is { AvatarSeed: 733, AvatarPreset: null } loaded && loaded.AvatarPixels!.SequenceEqual(pixels), "SQLite import preserves custom pixels, transparency and avatar seed exactly");
            check(File.ReadAllBytes(legacyPath).SequenceEqual(original), "migration preserves the original JSON as a rollback copy");
            var rotated = store.Recover(recovery)!;
            check(rotated.AccountId == id && store.Authenticate(token) is null && store.Authenticate(rotated.AccessToken)?.Id == id,
                "recovery after migration preserves identity and atomically revokes the old verifier");
            check(store.RotateAuthenticated(token) is null && store.Recover(recovery) is null, "rotated credentials cannot be reused for recovery or rotation");
            var game = new GameEngine(store, NullLogger<GameEngine>.Instance, TimeProvider.System);
            check(!game.TryAttach(new Peer(id), token) && game.TryAttach(new Peer(id), rotated.AccessToken), "a stale authenticated handshake cannot attach after credential revocation");
            var returned = store.Get(id)!; returned.AvatarPixels![0] = 7;
            check(store.Get(id)!.AvatarPixels![0] == pixels[0], "mutable profile arrays returned to callers cannot change stored avatar pixels");
            string backup = Path.Combine(root, "snapshot.db");
            store.Backup(backup);
            using (var snapshot = Open(backup))
            {
                using var query = snapshot.CreateCommand(); query.CommandText = "PRAGMA integrity_check";
                check(query.ExecuteScalar()?.ToString() == "ok", "online SQLite backup includes committed WAL data and passes integrity verification");
            }
            bool overwriteRefused = false;
            try { store.Backup(backup); } catch (IOException) { overwriteRefused = true; }
            check(overwriteRefused, "backup cannot overwrite an existing backup or the live database");
            // A real SQLite failure verifies commit-before-publication rather than
            // mirroring the persistence implementation in an in-memory mock.
            using (var admin = Open(Path.Combine(root, "accounts.db")))
            {
                using var trigger = admin.CreateCommand();
                trigger.CommandText = "CREATE TRIGGER reject_updates BEFORE UPDATE ON accounts BEGIN SELECT RAISE(ABORT,'fixture failure'); END";
                trigger.ExecuteNonQuery();
                bool failed = false;
                try { store.Update(id, "Uncommitted name", null, false); } catch (SqliteException) { failed = true; }
                check(failed && store.Get(id)!.DisplayName == "Migration artist", "failed profile writes roll back without publishing partial state");
                failed = false;
                try { store.Rotate(id); } catch (SqliteException) { failed = true; }
                check(failed && store.Authenticate(rotated.AccessToken)?.Id == id, "failed credential rotation leaves the last committed token valid");
                trigger.CommandText = "DROP TRIGGER reject_updates"; trigger.ExecuteNonQuery();
            }
            store.Update(id, "Migration artist", null, true, "job-paladin");
            store.Update(id, null, null, false, "job-paladin");
            check(store.Get(id) is { AvatarPreset: "job-paladin", AvatarPixels: null }, "SQLite stores an allow-listed job preset without artwork");
            Parallel.For(0, 24, i => store.Create("Concurrent artist " + i));
            check(store.Count() == 25, "concurrent account creation commits distinct identities without lost updates");
        }
        using (var reopened = new AccountStore(root))
            check(reopened.Count() == 25 && reopened.Authenticate(token) is null, "restart uses SQLite and never reimports stale JSON credentials");
        using (var first = new AccountStore(root))
        using (var second = new AccountStore(root))
        {
            Parallel.Invoke(() => { for (int i = 0; i < 12; i++) first.Create("Writer A " + i); },
                () => { for (int i = 0; i < 12; i++) second.Create("Writer B " + i); });
            check(first.Count() == 49 && second.Count() == 49, "SQLite serializes independent writers and both stores observe committed rows");
        }
        string invalidRoot = Path.Combine(working, "invalid-migration"); Directory.CreateDirectory(invalidRoot);
        var invalid = new Dictionary<Guid, StoredAccount>(legacy) { [Guid.NewGuid()] = legacy[id] };
        File.WriteAllBytes(Path.Combine(invalidRoot, "accounts.json"), JsonSerializer.SerializeToUtf8Bytes(invalid, Protocol.Json));
        bool rejected = false;
        try { using var ignored = new AccountStore(invalidRoot); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "invalid legacy identities fail closed instead of silently creating replacement accounts");
        File.WriteAllBytes(Path.Combine(invalidRoot, "accounts.json"), original);
        using (var retried = new AccountStore(invalidRoot)) check(retried.Count() == 1, "failed import rolls back its schema marker and retries cleanly after repair");
        string corruptRoot = Path.Combine(working, "corrupt-database"); Directory.CreateDirectory(corruptRoot);
        byte[] corrupt = Encoding.UTF8.GetBytes("not a database"); File.WriteAllBytes(Path.Combine(corruptRoot, "accounts.db"), corrupt);
        rejected = false;
        try { using var ignored = new AccountStore(corruptRoot); } catch (SqliteException) { rejected = true; }
        check(rejected && File.ReadAllBytes(Path.Combine(corruptRoot, "accounts.db")).SequenceEqual(corrupt), "corrupt SQLite files fail startup without being replaced");
        string unicodeRoot = Path.Combine(working, "unicode-storage");
        using (var unicode = new AccountStore(unicodeRoot))
        {
            var emoji = unicode.Create("🎨");
            check(unicode.Get(emoji.AccountId)?.DisplayName == "🎨", "valid supplementary Unicode names retain the existing UTF-16 name behavior");
        }
        using (var previousSchema = Open(Path.Combine(unicodeRoot, "accounts.db")))
        {
            using var schema = previousSchema.CreateCommand();
            schema.CommandText = "DROP TABLE account_names; DROP TABLE preferences; DROP TABLE lifetime; DROP TABLE relationships; PRAGMA user_version=1";
            schema.ExecuteNonQuery();
        }
        using (var upgraded = new AccountStore(unicodeRoot))
            check(upgraded.Count() == 1 && upgraded.Create("🎭").Profile.DisplayName == "🎭", "schema upgrade preserves existing rows and Unicode-compatible name constraints");
        using var bounded = new HttpResponseMessage { Content = new ByteArrayContent(new byte[4097]) };
        rejected = false;
        try { BoundedHttp.ReadAsync(bounded, 4096, CancellationToken.None).GetAwaiter().GetResult(); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "client rejects an oversized advertised HTTP response before buffering it");
        var state = new RoomView("TEST0001", new("Storage room"), "drawing", id, id, Guid.NewGuid(), 1,
            DateTimeOffset.UtcNow.AddSeconds(90), "____", null, false,
            [new(profile, 0, true, false, true, true)], DateTimeOffset.UtcNow);
        check(Protocol.ValidEvent(new("state", Room: state)), "valid bounded room packets pass the client trust boundary");
        check(!Protocol.ValidEvent(new("state", Room: state with { Players = null! })) &&
            !Protocol.ValidEvent(new("state", Room: state with { Players = new PlayerView[17] })), "null or oversized player collections cannot reach rendering");
        check(!Protocol.ValidEvent(new("cursor", Cursor: new(0, 0, "pencil", 255))) &&
            !Protocol.ValidEvent(new("cursor", Cursor: new(-1, 0, "pencil"))), "invalid cursor colors/coordinates cannot index client palette arrays");
        check(!Protocol.ValidEvent(new("canvas", Canvas: new byte[1])) &&
            !Protocol.ValidEvent(new("state", Room: state with { WordChoices = new string[9] })), "invalid canvas and offer sizes are rejected before UI dispatch");
        check(!Protocol.ValidEvent(new("error", Error: new string('x', 301))), "untrusted error text cannot create oversized notifications");
        using var fragments = new EmptyFragments();
        rejected = false;
        try { Protocol.ReceiveAsync(fragments, 12000, CancellationToken.None).GetAwaiter().GetResult(); } catch (InvalidDataException) { rejected = true; }
        check(rejected && fragments.Count == 257, "empty fragmented WebSocket messages cannot evade a bounded receive loop");
        var budgets = new AccountInputLimits();
        var shared = budgets.For(id);
        check(shared.Allow("profile") && !budgets.For(id).Allow("profile") && budgets.For(Guid.NewGuid()).Allow("profile"),
            "a reconnect shares the same account cooldown while different accounts retain independent budgets");
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open(); return connection;
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed class Peer(Guid id) : IGamePeer
    {
        public Guid Id => id;
        public string? RoomCode { get; set; }
        public void Send(ServerEvent message) { }
        public bool Allow(string type) => true;
        public void Close() { }
    }

    private sealed class EmptyFragments : WebSocket
    {
        public int Count;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override void Dispose() { }
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken cancel) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken cancel) => Task.CompletedTask;
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken cancel) => Task.CompletedTask;
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancel)
        { Count++; return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Text, false)); }
    }
}
