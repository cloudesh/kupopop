using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kupopop.Shared;

namespace Kupopop.Server;

public sealed record StoredAccount(PlayerProfile Profile, string AccessHash, string RecoveryHash);

public sealed class AccountStore
{
    private readonly object gate = new();
    private readonly string path;
    private readonly Dictionary<Guid, StoredAccount> accounts;
    private readonly Dictionary<string, Guid> access = new(StringComparer.Ordinal);

    public AccountStore(string directory)
    {
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        path = Path.Combine(directory, "accounts.json");
        // Fail closed if persistence is corrupt: never silently replace existing accounts.
        accounts = File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<Guid, StoredAccount>>(File.ReadAllBytes(path), Protocol.Json)
                ?? throw new InvalidDataException("Account store is empty or corrupt.")
            : [];
        foreach (var pair in accounts) access[pair.Value.AccessHash] = pair.Key;
    }

    public AccountCredentials Create(string displayName)
    {
        if (!Protocol.ValidName(displayName)) throw new ArgumentException("Use a display name of 2–40 characters.");
        lock (gate)
        {
            if (accounts.Count >= 10000) throw new InvalidOperationException("Account capacity reached; contact the host.");
            string token = Secret(), recovery = Secret();
            var profile = new PlayerProfile(Guid.NewGuid(), displayName.Trim(), RandomNumberGenerator.GetInt32(int.MaxValue));
            accounts[profile.Id] = new(profile, Hash(token), Hash(recovery));
            access[Hash(token)] = profile.Id;
            PersistOrRollback(profile.Id, null);
            return new(profile.Id, token, recovery, profile);
        }
    }

    public PlayerProfile? Authenticate(string? token)
    {
        if (token is null || token.Length is < 40 or > 100) return null;
        lock (gate) return access.TryGetValue(Hash(token), out var id) ? accounts[id].Profile : null;
    }

    public PlayerProfile? Get(Guid id) { lock (gate) return accounts.GetValueOrDefault(id)?.Profile; }

    public AccountCredentials? Recover(string code)
    {
        if (code.Length is < 40 or > 100) return null;
        string hash = Hash(code.Trim());
        lock (gate)
        {
            var entry = accounts.Values.FirstOrDefault(a => CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(a.RecoveryHash), Convert.FromHexString(hash)));
            if (entry is null) return null;
            string token = Secret(), recovery = Secret();
            access.Remove(entry.AccessHash);
            accounts[entry.Profile.Id] = entry with { AccessHash = Hash(token), RecoveryHash = Hash(recovery) };
            access[Hash(token)] = entry.Profile.Id;
            PersistOrRollback(entry.Profile.Id, entry);
            return new(entry.Profile.Id, token, recovery, entry.Profile);
        }
    }

    public PlayerProfile Update(Guid id, string? name, byte[]? avatar, bool reset, string? preset = null)
    {
        lock (gate)
        {
            var current = accounts[id];
            if (name is not null && !Protocol.ValidName(name)) throw new ArgumentException("Use a display name of 2–40 characters.");
            if (!Protocol.ValidAvatar(avatar)) throw new ArgumentException("Avatar format rejected.");
            if (preset is not null && AvatarPresets.Find(preset) is null) throw new ArgumentException("Avatar preset rejected.");
            if (avatar is not null && preset is not null) throw new ArgumentException("Choose a drawn avatar or a preset.");
            var profile = current.Profile with
            {
                DisplayName = name?.Trim() ?? current.Profile.DisplayName,
                AvatarPixels = reset || preset is not null ? null : avatar ?? current.Profile.AvatarPixels,
                AvatarPreset = reset || avatar is not null ? null : preset ?? current.Profile.AvatarPreset,
            };
            accounts[id] = current with { Profile = profile };
            PersistOrRollback(id, current);
            return profile;
        }
    }

    public AccountCredentials Rotate(Guid id)
    {
        lock (gate)
        {
            var entry = accounts[id];
            string token = Secret(), recovery = Secret();
            access.Remove(entry.AccessHash);
            accounts[id] = entry with { AccessHash = Hash(token), RecoveryHash = Hash(recovery) };
            access[Hash(token)] = id;
            PersistOrRollback(id, entry);
            return new(id, token, recovery, entry.Profile);
        }
    }

    private void Save()
    {
        string temp = path + ".tmp";
        using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            JsonSerializer.Serialize(file, accounts, Protocol.Json);
            file.Flush(true);
        }
        // Windows indexers/virus scanners can briefly hold a destination open.
        // Retry atomic replacement, preserving the original file on failure.
        for (int attempt = 0; ; attempt++)
        {
            try { File.Move(temp, path, true); break; }
            catch (Exception ex) when (attempt < 6 && ex is IOException or UnauthorizedAccessException)
            { Thread.Sleep(20 * (attempt + 1)); }
        }
    }

    private void PersistOrRollback(Guid id, StoredAccount? previous)
    {
        try { Save(); }
        catch
        {
            if (previous is null) accounts.Remove(id); else accounts[id] = previous;
            access.Clear();
            foreach (var pair in accounts) access[pair.Value.AccessHash] = pair.Key;
            throw;
        }
    }

    private static string Secret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
