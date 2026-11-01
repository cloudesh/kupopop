namespace Kupopop.Shared;

public static class CustomWordLists
{
    public const int MaximumTextLength = 1600;
    public const int MaximumWords = 64;
    public static bool ValidWord(string? value) => value is { Length: >= 2 and <= 48 } &&
        value.Count(char.IsLetter) >= 2 && value.All(c => char.IsLetterOrDigit(c) || c is ' ' or '\'' or '-' or '’');

    public static string[] Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        if (text.Length > MaximumTextLength) throw new ArgumentException("Custom word list is too long.");
        var words = text.Split(',').Select(w => w.Trim()).Where(w => w.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (words.Length > MaximumWords || words.Any(w => !ValidWord(w)))
            throw new ArgumentException("Use up to 64 words, each 2–48 characters, separated by commas. Letters, numbers, spaces, apostrophes and hyphens are allowed.");
        return words;
    }
}
