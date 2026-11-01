using System.Text;
using Kupopop.Shared;
using Microsoft.Data.Sqlite;

namespace Kupopop.Server;

public sealed class DisplayNameTakenException() : ArgumentException("That display name is unavailable. Please choose another.");

public sealed partial class AccountStore
{
    private static string NameKey(string name) => string.Join(' ', name.Normalize(NormalizationForm.FormKC)
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private void InitializeSocial()
    {
        using var versionQuery = Command("PRAGMA user_version");
        if (Convert.ToInt32(versionQuery.ExecuteScalar()) is >= 3 and <= SchemaVersion) return;
        using var transaction = database.BeginTransaction();
        Execute("""
            CREATE TABLE account_names (id TEXT PRIMARY KEY, name_key TEXT NOT NULL UNIQUE) STRICT;
            CREATE TABLE preferences (id TEXT PRIMARY KEY, track INTEGER NOT NULL DEFAULT 0, share INTEGER NOT NULL DEFAULT 0) STRICT;
            CREATE TABLE lifetime (id TEXT PRIMARY KEY, games INTEGER NOT NULL, wins INTEGER NOT NULL, points INTEGER NOT NULL, best INTEGER NOT NULL) STRICT;
            CREATE TABLE relationships (owner TEXT NOT NULL, target TEXT NOT NULL, accepted INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY(owner,target), CHECK(owner<>target)) STRICT;
            CREATE INDEX relationships_target ON relationships(target);
            """, transaction);
        var names = new List<(Guid Id, string Name)>();
        using (var command = Command("SELECT id,display_name FROM accounts ORDER BY rowid", transaction))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) names.Add((Guid.Parse(reader.GetString(0)), reader.GetString(1)));
        var reserved = names.Select(n => NameKey(n.Name)).ToHashSet(StringComparer.Ordinal);
        foreach (var (id, original) in names)
        {
            string name = original;
            if (NameInUse(name, id, transaction))
            {
                for (int suffix = 2; ; suffix++)
                {
                    string end = " ~" + suffix;
                    string stem = original[..Math.Min(original.Length, 40 - end.Length)];
                    if (char.IsHighSurrogate(stem[^1])) stem = stem[..^1];
                    name = stem + end;
                    if (!reserved.Contains(NameKey(name)) && !NameInUse(name, id, transaction)) break;
                }
            }
            SetNameKey(new(id, name, 0), transaction);
            using var update = Command("UPDATE accounts SET display_name=$name WHERE id=$id", transaction);
            update.Parameters.AddWithValue("$name", name); update.Parameters.AddWithValue("$id", id.ToString());
            update.ExecuteNonQuery();
        }
        Execute("PRAGMA user_version=3", transaction);
        transaction.Commit();
    }

    private void InitializeInvitationPreferences()
    {
        using var version = Command("PRAGMA user_version");
        if (Convert.ToInt32(version.ExecuteScalar()) >= 4) return;
        using var transaction = database.BeginTransaction();
        Execute("ALTER TABLE preferences ADD COLUMN invite_checks INTEGER NOT NULL DEFAULT 0; PRAGMA user_version=4;", transaction);
        transaction.Commit();
    }

    private bool NameInUse(string name, Guid id, SqliteTransaction transaction)
    {
        using var command = Command("SELECT 1 FROM account_names WHERE name_key=$key AND id<>$id", transaction);
        command.Parameters.AddWithValue("$key", NameKey(name)); command.Parameters.AddWithValue("$id", id.ToString());
        return command.ExecuteScalar() is not null;
    }

    private void EnsureNameAvailable(string name, Guid id, SqliteTransaction transaction)
    {
        if (NameInUse(name, id, transaction)) throw new DisplayNameTakenException();
    }

    private void SetNameKey(PlayerProfile profile, SqliteTransaction transaction)
    {
        using var command = Command("INSERT INTO account_names VALUES($id,$key) ON CONFLICT(id) DO UPDATE SET name_key=$key", transaction);
        command.Parameters.AddWithValue("$id", profile.Id.ToString()); command.Parameters.AddWithValue("$key", NameKey(profile.DisplayName));
        command.ExecuteNonQuery();
    }

    public ProfilePreferences Preferences(Guid id, SqliteTransaction? transaction = null)
    {
        lock (gate)
        {
            using var command = Command("SELECT track,share,invite_checks FROM preferences WHERE id=$id", transaction);
            command.Parameters.AddWithValue("$id", id.ToString());
            using var reader = command.ExecuteReader();
            return reader.Read() ? new(reader.GetBoolean(0), reader.GetBoolean(1), reader.GetBoolean(2)) : new(false, false);
        }
    }

    public void SetPreferences(Guid id, bool track, bool share, bool reset, bool? backgroundInvitations = null)
    {
        lock (gate)
        {
            using var transaction = database.BeginTransaction();
            bool invitations = backgroundInvitations ?? Preferences(id, transaction).BackgroundInvitations;
            using var command = Command("INSERT INTO preferences VALUES($id,$track,$share,$invites) ON CONFLICT(id) DO UPDATE SET track=$track,share=$share,invite_checks=$invites", transaction);
            command.Parameters.AddWithValue("$id", id.ToString()); command.Parameters.AddWithValue("$track", track); command.Parameters.AddWithValue("$share", share);
            command.Parameters.AddWithValue("$invites", invitations);
            command.ExecuteNonQuery();
            if (reset || !track)
            {
                using var clear = Command("DELETE FROM lifetime WHERE id=$id", transaction);
                clear.Parameters.AddWithValue("$id", id.ToString()); clear.ExecuteNonQuery();
                using var likes = Command("DELETE FROM drawing_likes WHERE id=$id", transaction);
                likes.Parameters.AddWithValue("$id", id.ToString()); likes.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    public LifetimeStats? Statistics(Guid id)
    {
        lock (gate)
        {
            if (!Preferences(id).TrackStats) return null;
            using var command = Command("SELECT games,wins,points,best FROM lifetime WHERE id=$id");
            command.Parameters.AddWithValue("$id", id.ToString());
            using var reader = command.ExecuteReader();
            return reader.Read() ? new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt32(3)) : new(0, 0, 0, 0);
        }
    }

    public void RecordMatch(SolveResult[] results, HashSet<Guid> optedAtStart)
    {
        lock (gate)
        {
            int winner = results.Max(r => r.Points);
            using var transaction = database.BeginTransaction();
            foreach (var result in results.Where(r => optedAtStart.Contains(r.PlayerId) && Preferences(r.PlayerId, transaction).TrackStats))
            {
                using var command = Command("""
                    INSERT INTO lifetime VALUES($id,1,$win,$points,$points)
                    ON CONFLICT(id) DO UPDATE SET games=games+1,wins=wins+$win,points=points+$points,best=max(best,$points)
                    """, transaction);
                command.Parameters.AddWithValue("$id", result.PlayerId.ToString()); command.Parameters.AddWithValue("$win", result.Points == winner);
                command.Parameters.AddWithValue("$points", result.Points); command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    public (Guid Id, string Relationship)[] Relationships(Guid id, SqliteTransaction? transaction = null)
    {
        lock (gate)
        {
            using var command = Command("SELECT owner,target,accepted FROM relationships WHERE owner=$id OR target=$id", transaction);
            command.Parameters.AddWithValue("$id", id.ToString());
            using var reader = command.ExecuteReader();
            var result = new List<(Guid, string)>();
            while (reader.Read())
            {
                bool outgoing = reader.GetString(0) == id.ToString();
                result.Add((Guid.Parse(reader.GetString(outgoing ? 1 : 0)), reader.GetBoolean(2) ? "friend" : outgoing ? "outgoing" : "incoming"));
            }
            return result.ToArray();
        }
    }

    public bool Friends(Guid a, Guid b) => Relationships(a).Any(r => r.Id == b && r.Relationship == "friend");

    public void ChangeFriend(Guid owner, Guid target, string action)
    {
        if (owner == target || Get(target) is null) throw new ArgumentException("Player unavailable.");
        lock (gate)
        {
            using var transaction = database.BeginTransaction();
            if (action == "request" && (Relationships(owner, transaction).Length >= 100 || Relationships(target, transaction).Length >= 100))
                throw new ArgumentException("Friend list or request limit reached.");
            string sql = action switch
            {
                "request" => "INSERT OR IGNORE INTO relationships VALUES($owner,$target,0)",
                "accept" => "UPDATE relationships SET accepted=1 WHERE owner=$target AND target=$owner AND accepted=0",
                "remove" => "DELETE FROM relationships WHERE (owner=$owner AND target=$target) OR (owner=$target AND target=$owner)",
                _ => throw new ArgumentException("Friend action rejected."),
            };
            if (action == "request" && Relationships(owner, transaction).Any(r => r.Id == target)) throw new ArgumentException("A request or friendship already exists.");
            using var command = Command(sql, transaction);
            command.Parameters.AddWithValue("$owner", owner.ToString()); command.Parameters.AddWithValue("$target", target.ToString());
            if (command.ExecuteNonQuery() == 0) throw new ArgumentException("Friend action is no longer available.");
            transaction.Commit();
        }
    }
}
