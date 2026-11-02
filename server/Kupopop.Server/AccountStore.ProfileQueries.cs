using Kupopop.Shared;

namespace Kupopop.Server;

public sealed partial class AccountStore
{
    /// <summary>One consistent query for the bounded relationship/recent-player panel.</summary>
    public Dictionary<Guid, (PlayerProfile Profile, ProfilePreferences Preferences)> SocialProfiles(Guid[] ids)
    {
        if (ids.Length > 130)
        {
            throw new ArgumentException("Social profile limit exceeded.");
        }

        if (ids.Length == 0)
        {
            return [];
        }

        lock (gate)
        {
            var parameters = Enumerable.Range(0, ids.Length).Select(i => "$id" + i).ToArray();
            using var query = Command($"""
                SELECT a.id,a.display_name,a.avatar_seed,a.avatar_pixels,a.avatar_preset,
                    coalesce(p.track,0),coalesce(p.share,0),coalesce(p.invite_checks,0)
                FROM accounts a LEFT JOIN preferences p ON p.id=a.id
                WHERE a.id IN ({string.Join(',', parameters)})
                """);
            for (int i = 0; i < ids.Length; i++)
            {
                query.Parameters.AddWithValue(parameters[i], ids[i].ToString());
            }

            using var reader = query.ExecuteReader();
            var result = new Dictionary<Guid, (PlayerProfile, ProfilePreferences)>();
            while (reader.Read())
            {
                var profile = new PlayerProfile(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : (byte[])reader[3], reader.IsDBNull(4) ? null : reader.GetString(4));
                result.Add(profile.Id, (profile, new(reader.GetBoolean(5), reader.GetBoolean(6), reader.GetBoolean(7))));
            }
            return result;
        }
    }
}
