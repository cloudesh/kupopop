using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kupopop.Shared;
using Microsoft.Data.Sqlite;

namespace Kupopop.Server;

// Retained only for importing pre-SQLite installations.
public sealed record StoredAccount(PlayerProfile Profile, string AccessHash, string RecoveryHash);

/// <summary>Transactional account persistence. Game rooms and scores remain ephemeral.</summary>
public sealed partial class AccountStore : IDisposable
{
    private const int SchemaVersion = 4;
    private const int MaximumAccounts = 10_000;
    private const string ProfileColumns = "id, display_name, avatar_seed, avatar_pixels, avatar_preset";
    private readonly object gate = new();
    private readonly SqliteConnection database;

    public AccountStore(string directory)
    {
        Directory.CreateDirectory(directory);
        Restrict(directory, directory: true);
        string path = Path.Combine(directory, "accounts.db");
        database = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 5,
        }.ToString());
        try
        {
            database.Open();
            Restrict(path);
            Execute("PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA trusted_schema=OFF;");
            Initialize(directory);
            InitializeSocial();
            InitializeInvitationPreferences();
            VerifyIntegrity();
        }
        catch { database.Dispose(); throw; }
    }

    public AccountCredentials Create(string displayName, bool shareProfile = false)
    {
        ValidateName(displayName);
        lock (gate)
        {
            using var transaction = database.BeginTransaction();
            if (Count(transaction) >= MaximumAccounts) throw new InvalidOperationException("Account capacity reached.");
            string access = Secret(), recovery = Secret();
            var profile = new PlayerProfile(Guid.NewGuid(), displayName.Trim(), RandomNumberGenerator.GetInt32(int.MaxValue));
            EnsureNameAvailable(profile.DisplayName, profile.Id, transaction);
            Insert(new(profile, Hash(access), Hash(recovery)), transaction);
            SetNameKey(profile, transaction);
            using (var preferences = Command("INSERT INTO preferences(id,track,share,invite_checks) VALUES($id,0,$share,0)", transaction))
            {
                preferences.Parameters.AddWithValue("$id", profile.Id.ToString()); preferences.Parameters.AddWithValue("$share", shareProfile);
                preferences.ExecuteNonQuery();
            }
            transaction.Commit();
            return new(profile.Id, access, recovery, profile);
        }
    }

    public PlayerProfile? Authenticate(string? token)
    {
        if (token is not { Length: >= 40 and <= 100 }) return null;
        lock (gate) return Find("access_hash", Hash(token));
    }

    public PlayerProfile? Get(Guid id)
    {
        lock (gate) return Find("id", id.ToString());
    }

    public AccountCredentials? Recover(string code)
    {
        if (code is not { Length: >= 40 and <= 100 }) return null;
        lock (gate)
        {
            using var transaction = database.BeginTransaction();
            var profile = Find("recovery_hash", Hash(code.Trim()), transaction);
            if (profile is null) return null;
            return Rotate(profile, transaction);
        }
    }

    public AccountCredentials Rotate(Guid id)
    {
        lock (gate)
        {
            using var transaction = database.BeginTransaction();
            var profile = Find("id", id.ToString(), transaction) ?? throw new ArgumentException("Account not found.");
            return Rotate(profile, transaction);
        }
    }

    public AccountCredentials? RotateAuthenticated(string? accessToken)
    {
        if (accessToken is not { Length: >= 40 and <= 100 }) return null;
        lock (gate)
        {
            using var transaction = database.BeginTransaction();
            var profile = Find("access_hash", Hash(accessToken), transaction);
            return profile is null ? null : Rotate(profile, transaction);
        }
    }

    private AccountCredentials Rotate(PlayerProfile profile, SqliteTransaction transaction)
    {
        string access = Secret(), recovery = Secret();
        using var command = Command("UPDATE accounts SET access_hash=$access, recovery_hash=$recovery WHERE id=$id", transaction);
        command.Parameters.AddWithValue("$id", profile.Id.ToString());
        command.Parameters.AddWithValue("$access", Hash(access));
        command.Parameters.AddWithValue("$recovery", Hash(recovery));
        command.ExecuteNonQuery();
        transaction.Commit();
        return new(profile.Id, access, recovery, profile);
    }

    public PlayerProfile Update(Guid id, string? name, byte[]? avatar, bool reset, string? preset = null)
    {
        if (name is not null) ValidateName(name);
        if (!Protocol.ValidAvatar(avatar)) throw new ArgumentException("Avatar format rejected.");
        if (preset is not null && AvatarPresets.Find(preset) is null) throw new ArgumentException("Avatar preset rejected.");
        if (avatar is not null && preset is not null) throw new ArgumentException("Choose a drawn avatar or a preset.");
        lock (gate)
        {
            using var transaction = database.BeginTransaction();
            var current = Find("id", id.ToString(), transaction) ?? throw new ArgumentException("Account not found.");
            var profile = current with
            {
                DisplayName = name?.Trim() ?? current.DisplayName,
                AvatarPixels = reset || preset is not null ? null : avatar ?? current.AvatarPixels,
                AvatarPreset = reset || avatar is not null ? null : preset ?? current.AvatarPreset,
            };
            EnsureNameAvailable(profile.DisplayName, id, transaction);
            SetNameKey(profile, transaction);
            using var command = Command("UPDATE accounts SET display_name=$name, avatar_pixels=$pixels, avatar_preset=$preset WHERE id=$id", transaction);
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$name", profile.DisplayName);
            command.Parameters.AddWithValue("$pixels", (object?)profile.AvatarPixels ?? DBNull.Value);
            command.Parameters.AddWithValue("$preset", (object?)profile.AvatarPreset ?? DBNull.Value);
            command.ExecuteNonQuery();
            transaction.Commit();
            return profile;
        }
    }

    public int Count() => Count(null);

    private int Count(SqliteTransaction? transaction)
    {
        lock (gate)
        {
            using var command = Command("SELECT COUNT(*) FROM accounts", transaction);
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    public void VerifyIntegrity()
    {
        lock (gate)
        {
            using var command = Command("PRAGMA quick_check");
            if (!string.Equals(command.ExecuteScalar()?.ToString(), "ok", StringComparison.Ordinal))
                throw new InvalidDataException("Account database integrity check failed.");
            if (Count() > MaximumAccounts) throw new InvalidDataException("Account capacity exceeded.");
        }
    }

    public void Backup(string destination)
    {
        lock (gate)
        {
            // CreateNew prevents accidentally overwriting the live DB or an older backup.
            using (File.Open(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            Restrict(destination);
            using var backup = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = destination, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
            backup.Open();
            database.BackupDatabase(backup);
        }
    }

    private void Initialize(string directory)
    {
        using var versionQuery = Command("PRAGMA user_version");
        int version = Convert.ToInt32(versionQuery.ExecuteScalar());
        if (version is 2 or 3 or SchemaVersion) return;
        if (version == 1)
        {
            // SQLite counts Unicode scalars; .NET string limits count UTF-16
            // units. A single emoji is a valid two-unit display name.
            using var upgrade = database.BeginTransaction();
            CreateTable("accounts_upgrade", upgrade);
            Execute("INSERT INTO accounts_upgrade SELECT * FROM accounts; DROP TABLE accounts; ALTER TABLE accounts_upgrade RENAME TO accounts;", upgrade);
            Execute($"PRAGMA user_version=2", upgrade);
            upgrade.Commit();
            return;
        }
        if (version != 0) throw new InvalidDataException("Unsupported account database version. Use a compatible server release.");

        using var transaction = database.BeginTransaction();
        CreateTable("accounts", transaction, ifMissing: true);
        if (Count(transaction) != 0) throw new InvalidDataException("Unversioned account database contains data; automatic import refused.");
        ImportLegacy(Path.Combine(directory, "accounts.json"), transaction);
        Execute($"PRAGMA user_version=2", transaction);
        transaction.Commit();
    }

    private void CreateTable(string table, SqliteTransaction transaction, bool ifMissing = false)
    {
        Execute("""
            CREATE TABLE
            """ + (ifMissing ? " IF NOT EXISTS " : " ") + table + """
             (
                id TEXT PRIMARY KEY NOT NULL,
                display_name TEXT NOT NULL CHECK(length(display_name) BETWEEN 1 AND 40),
                avatar_seed INTEGER NOT NULL,
                avatar_pixels BLOB CHECK(avatar_pixels IS NULL OR length(avatar_pixels)=4096),
                avatar_preset TEXT,
                access_hash TEXT NOT NULL UNIQUE CHECK(length(access_hash)=64),
                recovery_hash TEXT NOT NULL UNIQUE CHECK(length(recovery_hash)=64)
            ) STRICT;
            """, transaction);
    }

    private void ImportLegacy(string path, SqliteTransaction transaction)
    {
        if (!File.Exists(path)) return;
        if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException("Legacy account file exceeds the migration limit.");
        Dictionary<Guid, StoredAccount> source;
        try
        {
            source = JsonSerializer.Deserialize<Dictionary<Guid, StoredAccount>>(File.ReadAllBytes(path), Protocol.Json)
                ?? throw new InvalidDataException("Legacy account file is empty.");
        }
        catch (JsonException) { throw new InvalidDataException("Legacy account file is invalid; import refused."); }
        if (source.Count > MaximumAccounts) throw new InvalidDataException("Legacy account capacity exceeded.");
        foreach (var (id, account) in source)
        {
            if (account?.Profile is not { } profile || id == Guid.Empty || id != profile.Id ||
                !Protocol.ValidName(profile.DisplayName) || !Protocol.ValidAvatar(profile.AvatarPixels) ||
                (profile.AvatarPreset is not null && AvatarPresets.Find(profile.AvatarPreset) is null) ||
                !ValidHash(account.AccessHash) || !ValidHash(account.RecoveryHash))
                throw new InvalidDataException("Legacy account validation failed; import refused.");
            Insert(account, transaction);
        }
        // The original file remains untouched as the rollback copy. Schema version
        // makes this a one-time import, even if that copy is present on every boot.
        Restrict(path);
    }

    private void Insert(StoredAccount account, SqliteTransaction transaction)
    {
        using var command = Command("""
            INSERT INTO accounts (id, display_name, avatar_seed, avatar_pixels, avatar_preset, access_hash, recovery_hash)
            VALUES ($id, $name, $seed, $pixels, $preset, $access, $recovery)
            """, transaction);
        command.Parameters.AddWithValue("$id", account.Profile.Id.ToString());
        command.Parameters.AddWithValue("$name", account.Profile.DisplayName);
        command.Parameters.AddWithValue("$seed", account.Profile.AvatarSeed);
        command.Parameters.AddWithValue("$pixels", (object?)account.Profile.AvatarPixels ?? DBNull.Value);
        command.Parameters.AddWithValue("$preset", (object?)account.Profile.AvatarPreset ?? DBNull.Value);
        command.Parameters.AddWithValue("$access", account.AccessHash);
        command.Parameters.AddWithValue("$recovery", account.RecoveryHash);
        command.ExecuteNonQuery();
    }

    private PlayerProfile? Find(string column, string value, SqliteTransaction? transaction = null)
    {
        // Column names are internal literals; every external value is a parameter.
        using var command = Command($"SELECT {ProfileColumns} FROM accounts WHERE {column}=$value", transaction);
        command.Parameters.AddWithValue("$value", value);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetInt32(2),
            reader.IsDBNull(3) ? null : (byte[])reader[3], reader.IsDBNull(4) ? null : reader.GetString(4)) : null;
    }

    private SqliteCommand Command(string sql, SqliteTransaction? transaction = null)
    {
        var command = database.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        return command;
    }

    private void Execute(string sql, SqliteTransaction? transaction = null)
    {
        using var command = Command(sql, transaction);
        command.ExecuteNonQuery();
    }

    private static void ValidateName(string name)
    {
        if (!Protocol.ValidName(name?.Trim())) throw new ArgumentException("Use a display name of 2–40 characters.");
    }

    private static bool ValidHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static string Secret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void Restrict(string path, bool directory = false)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | (directory ? UnixFileMode.UserExecute : 0));
    }

    public void Dispose() { lock (gate) database.Dispose(); }
}
