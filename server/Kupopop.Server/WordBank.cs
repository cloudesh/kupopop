using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kupopop.Shared;

namespace Kupopop.Server;

public sealed record ServerWordList(string Id, string Name, bool DefaultEnabled, bool Complex, string[] Words);

/// <summary>Private host data. Only catalog metadata may cross the protocol boundary.</summary>
public sealed class WordBank
{
    private readonly ServerWordList[] lists;
    public WordBank(ServerWordList[] source)
    {
        if (source.Length is < 1 or > 32) throw new InvalidDataException("Word-list count must be 1 to 32.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        lists = source.Select(list =>
        {
            if (list is null || !Regex.IsMatch(list.Id ?? "", "^[a-z0-9-]{1,40}$") || !ids.Add(list.Id!) ||
                !Protocol.ValidName(list.Name) || list.Words is null || list.Words.Length is < 2 or > 2000 ||
                list.Words.Any(w => string.IsNullOrWhiteSpace(w) || w.Length > 64 || w.Any(char.IsControl)))
                throw new InvalidDataException("Invalid word-list metadata or entries.");
            var words = list.Words.Select(w => w.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (words.Length < 2) throw new InvalidDataException("Each list needs two distinct words.");
            return list with { Words = words };
        }).ToArray();
        if (!lists.Any(l => l.DefaultEnabled)) throw new InvalidDataException("Enable at least one default list.");
    }
    public WordListInfo[] Catalog => lists.Select(l => new WordListInfo(l.Id, l.Name, l.Words.Length, l.DefaultEnabled, l.Complex)).ToArray();
    public string[] Selection(string[]? requested)
    {
        var selected = requested ?? lists.Where(l => l.DefaultEnabled).Select(l => l.Id).ToArray();
        if (selected.Length is < 1 or > 32 || selected.Any(id => !lists.Any(l => l.Id == id)) ||
            selected.Distinct(StringComparer.Ordinal).Count() != selected.Length)
            throw new ArgumentException("Choose at least one valid word list.");
        return (string[])selected.Clone();
    }
    public string Choose(string[]? selected, string previous)
    {
        string[] ids = Selection(selected);
        string[] pool = lists.Where(l => ids.Contains(l.Id)).SelectMany(l => l.Words)
            .Where(w => !w.Equals(previous, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return pool[RandomNumberGenerator.GetInt32(pool.Length)];
    }
    public string[] Choices(string[]? selected, string previous, int count = 4)
    {
        if (count is < 1 or > 8) throw new ArgumentException("Choose between one and eight word offers.");
        string[] ids = Selection(selected);
        var pool = lists.Where(l => ids.Contains(l.Id)).SelectMany(l => l.Words).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (pool.Length < count) throw new ArgumentException($"Select lists with at least {count} distinct words.");
        RandomNumberGenerator.Shuffle(pool.AsSpan());
        // Avoid the previous answer when there are enough alternatives.
        var alternatives = pool.Where(w => !w.Equals(previous, StringComparison.OrdinalIgnoreCase)).ToArray();
        return (alternatives.Length >= count ? alternatives : pool).Take(count).ToArray();
    }
    public static WordBank Starter() => new([new("starter", "FFXIV Starter", true, false, ["Moogle", "Chocobo", "Cactuar", "Spriggan"])]);
    public static WordBank Open(string directory)
    {
        string path = Path.Combine(directory, "word-lists.json");
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(directory);
            var initial = Starter();
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            JsonSerializer.Serialize(file, initial.lists, Protocol.Json);
            return initial;
        }
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("Word-list file exceeds limit.");
        var source = JsonSerializer.Deserialize<ServerWordList[]>(File.ReadAllBytes(path), Protocol.Json)
            ?? throw new InvalidDataException("Word-list file is empty.");
        // Upgrade only our untouched three-word starter, preserving custom and
        // maintained banks. Four distinct offers require one additional entry.
        if (source is [{ Id: "starter", Name: "FFXIV Starter", DefaultEnabled: true, Complex: false, Words: { Length: 3 } old }] &&
            old.ToHashSet(StringComparer.Ordinal).SetEquals(new[] { "Moogle", "Chocobo", "Cactuar" }))
        {
            source = Starter().lists;
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(source, Protocol.Json));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temp, path, true);
        }
        return new(source);
    }
}
