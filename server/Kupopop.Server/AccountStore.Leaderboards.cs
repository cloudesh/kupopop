using Kupopop.Shared;

namespace Kupopop.Server;

public sealed partial class AccountStore
{
    private void InitializeLeaderboards()
    {
        using var version = Command("PRAGMA user_version");
        if (Convert.ToInt32(version.ExecuteScalar()) == SchemaVersion) return;
        using var transaction = database.BeginTransaction();
        Execute("""
            CREATE TABLE IF NOT EXISTS leaderboard_preferences (id TEXT PRIMARY KEY, named INTEGER NOT NULL DEFAULT 0,
                hidden INTEGER NOT NULL DEFAULT 0, portrait INTEGER NOT NULL DEFAULT 1) STRICT;
            CREATE TABLE IF NOT EXISTS drawing_likes (id TEXT PRIMARY KEY, total INTEGER NOT NULL CHECK(total>=0)) STRICT;
            UPDATE accounts SET avatar_preset=NULL WHERE avatar_preset GLOB 'job-*';
            PRAGMA user_version=5;
            """, transaction);
        transaction.Commit();
    }

    public void SetLeaderboardPreference(Guid id, string preference, bool enabled)
    {
        string column = preference switch { "name" => "named", "hidden" => "hidden", "portrait" => "portrait", _ => throw new ArgumentException("Invalid preference.") };
        lock (gate)
        {
            using var command = Command($"INSERT INTO leaderboard_preferences(id,{column}) VALUES($id,$enabled) ON CONFLICT(id) DO UPDATE SET {column}=$enabled");
            command.Parameters.AddWithValue("$id", id.ToString()); command.Parameters.AddWithValue("$enabled", enabled);
            command.ExecuteNonQuery();
        }
    }

    public void RecordDrawingLike(Guid drawer)
    {
        lock (gate)
        {
            if (!Preferences(drawer).TrackStats) return;
            using var command = Command("INSERT INTO drawing_likes VALUES($id,1) ON CONFLICT(id) DO UPDATE SET total=total+1");
            command.Parameters.AddWithValue("$id", drawer.ToString()); command.ExecuteNonQuery();
        }
    }

    public LeaderboardView Leaderboard(Guid viewer, string metric, int page)
    {
        string column = metric switch
        {
            "best" => "best", "points" => "points", "games" => "games", "likes" => "likes",
            _ => throw new ArgumentException("Unknown leaderboard."),
        };
        if (page is < 0 or > 199) throw new ArgumentException("Invalid leaderboard page.");
        lock (gate)
        {
            using var command = Command($"""
                SELECT CASE WHEN b.named=1 THEN a.display_name ELSE 'Anonymous' END,
                    CASE WHEN coalesce(b.portrait,1)=1 THEN a.avatar_seed ELSE 123 END,
                    CASE WHEN coalesce(b.portrait,1)=1 THEN a.avatar_preset ELSE NULL END,
                    coalesce(l.best,0) AS best,coalesce(l.points,0) AS points,coalesce(l.games,0) AS games,coalesce(d.total,0) AS likes,
                    CASE WHEN coalesce(b.portrait,1)=1 THEN a.avatar_pixels ELSE NULL END
                FROM accounts a LEFT JOIN leaderboard_preferences b ON b.id=a.id
                JOIN preferences p ON p.id=a.id AND p.track=1
                LEFT JOIN lifetime l ON l.id=a.id LEFT JOIN drawing_likes d ON d.id=a.id
                WHERE coalesce(b.hidden,0)=0
                ORDER BY {column} DESC,a.id LIMIT 51 OFFSET $offset
                """);
            command.Parameters.AddWithValue("$offset", page * 50);
            using var reader = command.ExecuteReader();
            var rows = new List<LeaderboardEntry>();
            while (reader.Read()) rows.Add(new(reader.GetString(0), reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetInt32(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6), reader.IsDBNull(7) ? null : (byte[])reader.GetValue(7)));
            reader.Close();
            using var preferences = Command("SELECT named,hidden,portrait FROM leaderboard_preferences WHERE id=$id");
            preferences.Parameters.AddWithValue("$id", viewer.ToString());
            using var settings = preferences.ExecuteReader();
            bool found = settings.Read();
            return new(metric, page, rows.Take(50).ToArray(), rows.Count > 50,
                found && settings.GetBoolean(0), found && settings.GetBoolean(1), !found || settings.GetBoolean(2));
        }
    }
}
