using System.Text;

namespace Kupopop.Server;

public static class GuessMatching
{
    public static bool ValidCustomWord(string? value) => value is { Length: >= 2 and <= 48 } &&
        value.Count(char.IsLetter) >= 2 && value.All(c => char.IsLetterOrDigit(c) || c is ' ' or '\'' or '-' or '’');

    public static string Normalize(string value)
    {
        var result = new StringBuilder();
        foreach (char c in value.Normalize(NormalizationForm.FormD))
            if (char.IsLetterOrDigit(c)) result.Append(char.ToUpperInvariant(c));
        return result.ToString();
    }

    // Inputs are already normalized. Accept one insertion, deletion,
    // substitution or adjacent transposition; never accept it as a solve.
    public static bool IsNear(string guess, string answer)
    {
        if (Math.Min(guess.Length, answer.Length) < 3 || Math.Abs(guess.Length - answer.Length) > 1) return false;
        int first = 0, limit = Math.Min(guess.Length, answer.Length);
        while (first < limit && guess[first] == answer[first]) first++;
        if (guess.Length == answer.Length)
        {
            if (first == limit) return false;
            if (guess.AsSpan(first + 1).SequenceEqual(answer.AsSpan(first + 1))) return true;
            return first + 1 < limit && guess[first] == answer[first + 1] && guess[first + 1] == answer[first] &&
                guess.AsSpan(first + 2).SequenceEqual(answer.AsSpan(first + 2));
        }
        string longer = guess.Length > answer.Length ? guess : answer;
        string shorter = guess.Length > answer.Length ? answer : guess;
        return longer.AsSpan(first + 1).SequenceEqual(shorter.AsSpan(first));
    }
}
