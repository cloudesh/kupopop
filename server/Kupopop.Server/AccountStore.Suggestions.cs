using Kupopop.Shared;

namespace Kupopop.Server;

public sealed record SuggestionEntry(long Number, DateTimeOffset SubmittedAt, string Kind, string? Category, string Message);

public sealed partial class AccountStore
{
    private void InitializeSuggestions()
    {
        using var version = Command("PRAGMA user_version");
        if (Convert.ToInt32(version.ExecuteScalar()) == SchemaVersion) return;
        using var transaction = database.BeginTransaction();
        Execute("""
            CREATE TABLE IF NOT EXISTS suggestions (number INTEGER PRIMARY KEY, account TEXT NOT NULL, submitted INTEGER NOT NULL,
                kind TEXT NOT NULL CHECK(kind IN ('word','suggestion')), category TEXT, message TEXT NOT NULL CHECK(length(message) BETWEEN 2 AND 1000)) STRICT;
            CREATE INDEX IF NOT EXISTS suggestions_account_time ON suggestions(account,submitted);
            PRAGMA user_version=6;
            """, transaction);
        transaction.Commit();
    }

    public void SubmitSuggestion(Guid account, string? kind, string? category, string? message, DateTimeOffset now)
    {
        string text = message?.Trim() ?? "";
        if (kind is not ("word" or "suggestion") || text.Length > 1000 || text.Length < (kind == "word" ? 2 : 5) ||
            text.Any(c => char.IsControl(c) && c != '\n') || kind == "word" && !CustomWordLists.ValidWord(text))
            throw new ArgumentException("Check your suggestion: words use 2–48 characters; general suggestions use 5–1,000 characters.");
        if ((kind == "word" || category is not null) && !Protocol.ValidName(category)) throw new ArgumentException("Choose or enter a category of 2–40 characters.");
        lock (gate)
        {
            using var transaction = database.BeginTransaction();
            using var quota = Command("SELECT count(*),coalesce(max(submitted),0) FROM suggestions WHERE account=$id AND submitted>$day", transaction);
            quota.Parameters.AddWithValue("$id", account.ToString()); quota.Parameters.AddWithValue("$day", now.AddDays(-1).ToUnixTimeSeconds());
            using (var reader = quota.ExecuteReader())
            {
                reader.Read();
                if (reader.GetInt64(0) >= 5 || reader.GetInt64(1) > now.AddMinutes(-1).ToUnixTimeSeconds())
                    throw new ArgumentException("Please wait a minute between suggestions. You can send five per day.");
            }
            using var capacity = Command("SELECT count(*) FROM suggestions", transaction);
            if (Convert.ToInt64(capacity.ExecuteScalar()) >= 5000) throw new ArgumentException("The suggestion inbox is full. Please try again later.");
            using var insert = Command("INSERT INTO suggestions(account,submitted,kind,category,message) VALUES($id,$at,$kind,$category,$text)", transaction);
            insert.Parameters.AddWithValue("$id", account.ToString()); insert.Parameters.AddWithValue("$at", now.ToUnixTimeSeconds());
            insert.Parameters.AddWithValue("$kind", kind!); insert.Parameters.AddWithValue("$category", (object?)category ?? DBNull.Value);
            insert.Parameters.AddWithValue("$text", text); insert.ExecuteNonQuery();
            transaction.Commit();
        }
    }

    // Operator-only CLI inbox. Never mapped to HTTP, WebSockets or diagnostics.
    public SuggestionEntry[] ReadSuggestions()
    {
        lock (gate)
        {
            using var query = Command("SELECT number,submitted,kind,category,message FROM suggestions ORDER BY number DESC LIMIT 100");
            using var reader = query.ExecuteReader();
            var entries = new List<SuggestionEntry>();
            while (reader.Read()) entries.Add(new(reader.GetInt64(0), DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(1)),
                reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4)));
            return entries.ToArray();
        }
    }
}
