using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Kupopop.Server;
using Kupopop.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;

static class SocialChecks
{
    public static void Run(string working, Action<bool, string> check)
    {
        using var accounts = new AccountStore(Path.Combine(working, "social"));
        var a = accounts.Create("Mog Artist");
        var b = accounts.Create("Another Artist");
        var c = accounts.Create("Unknown Artist");
        bool rejected = false;
        try { accounts.Create("  MOG   ARTIST  "); } catch (DisplayNameTakenException) { rejected = true; }
        check(rejected && accounts.Count() == 3, "normalized names are unique without partially creating accounts");
        rejected = false;
        try { accounts.Update(b.AccountId, "Ｍｏｇ Artist", null, false); } catch (DisplayNameTakenException) { rejected = true; }
        check(rejected && accounts.Get(b.AccountId)!.DisplayName == b.Profile.DisplayName, "compatibility-normalized rename conflicts preserve the prior profile");
        var clock = new TestClock();
        var game = new GameEngine(accounts, NullLogger<GameEngine>.Instance, clock);
        var pa = new TestPeer(a.AccountId); var pb = new TestPeer(b.AccountId); var pc = new TestPeer(c.AccountId);
        game.Attach(pa); game.Attach(pb); game.Attach(pc);
        SocialView Social(TestPeer p) => p.Events.Last(e => e.Social is not null).Social!;
        game.Handle(pa, new("friend-request", TargetId: c.AccountId));
        check(pa.Events[^1].Type == "error" && accounts.Relationships(a.AccountId).Length == 0, "arbitrary account IDs cannot be used for player discovery or unsolicited requests");
        game.Handle(pa, new("view-profile", TargetId: c.AccountId));
        check(pa.Events[^1].Type == "error", "profile lookup rejects unknown players without exposing their account");
        game.Handle(pa, new("create", Options: new("Social private room", Rounds: 1, TurnSeconds: 15)));
        string code = pa.RoomCode!;
        game.Handle(pb, new("join", Text: code));
        game.Handle(pa, new("social"));
        check(Social(pa).People.Single().Relationship == "recent" && !Social(pa).People.Single().Online,
            "recent partners are private and do not expose presence without mutual friendship");
        game.Handle(pa, new("friend-request", TargetId: b.AccountId));
        check(Social(pb).People.Single().Relationship == "incoming" && Social(pa).People.Single().Relationship == "outgoing",
            "friend requests have distinct incoming and outgoing states");
        game.Handle(pa, new("friend-accept", TargetId: b.AccountId));
        check(!accounts.Friends(a.AccountId, b.AccountId), "request senders cannot accept their own request");
        game.Handle(pb, new("friend-accept", TargetId: a.AccountId));
        check(accounts.Friends(a.AccountId, b.AccountId) && Social(pa).People.Single().Online,
            "recipient acceptance creates mutual friendship and scoped live presence");
        using (var secondStore = new AccountStore(Path.Combine(working, "social")))
            check(secondStore.Friends(a.AccountId, b.AccountId), "accepted friendships persist across store instances");
        game.Handle(pa, new("view-profile", TargetId: b.AccountId));
        check(pa.Events[^1].SharedProfile is { Visible: false, Stats: null }, "profiles and lifetime statistics default private and disabled");
        game.Handle(pb, new("profile-privacy", Enabled: true, Shared: true));
        game.Handle(pa, new("view-profile", TargetId: b.AccountId));
        check(pa.Events[^1].SharedProfile is { Visible: true, Stats.Games: 0, Preferences: null }, "explicit profile sharing exposes totals without another user's private settings");
        game.Handle(pb, new("leave"));
        game.Handle(pa, new("invite", TargetId: b.AccountId));
        var invite = Social(pb).Invitations.Single();
        check(pb.Events.Any(e => e.Invitation?.Id == invite.Id) && !JsonSerializer.Serialize(invite).Contains(code),
            "invitations use expiring opaque IDs and never expose room join codes");
        game.Handle(pc, new("accept-invite", TargetId: invite.Id));
        check(pc.RoomCode is null, "an invitation is bound to its authenticated recipient");
        game.Handle(pb, new("accept-invite", TargetId: invite.Id));
        check(pb.RoomCode == code && Social(pb).Invitations.Length == 0, "accepting an invitation joins exactly once through normal room validation");
        game.Handle(pb, new("leave")); clock.Advance(4);
        game.Handle(pa, new("invite", TargetId: b.AccountId));
        var expired = Social(pb).Invitations.Single(); clock.Advance(301);
        game.Handle(pb, new("accept-invite", TargetId: expired.Id));
        check(pb.RoomCode is null && pb.Events[^1].Type == "error", "expired invitations cannot rejoin a room");
        game.Handle(pb, new("join", Text: code));
        var status = game.GetStatus(TimeSpan.FromSeconds(17));
        string json = JsonSerializer.Serialize(status);
        check(status.ConnectedUsers == 3 && status.Rooms.Single().Connected == 2 && status.LobbyUsers == 1 &&
            !json.Contains(code) && !json.Contains("Artist") && !json.Contains(a.AccountId.ToString()),
            "monitor reports room occupancy and connected counts without names, join codes or player IDs");
        string path = Path.Combine(working, "monitor", "status.json");
        ServerMonitor.Write(path, status);
        check(ServerMonitor.Read(path).Rooms.Single().Capacity == 16, "private atomic monitor snapshots round-trip");
        game.Handle(pa, new("start"));
        for (int i = 0; i < 2; i++)
        {
            var drawer = pa.State.DrawerId == pa.Id ? pa : pb;
            game.Handle(drawer, new("choose-word", Text: drawer.State.WordChoices![0], TurnId: drawer.State.TurnId));
            clock.Advance(16); game.Tick(); clock.Advance(7); game.Tick();
        }
        check(accounts.Statistics(b.AccountId)?.Games == 1 && accounts.Statistics(a.AccountId) is null,
            "completed matches record opted-in lifetime totals without collecting opted-out players' totals");
        game.Tick();
        check(accounts.Statistics(b.AccountId)?.Games == 1, "repeated game ticks cannot count a completed match twice");
        accounts.SetPreferences(b.AccountId, true, true, reset: true);
        check(accounts.Statistics(b.AccountId)?.Games == 0, "players can reset their own lifetime totals");
        accounts.SetPreferences(b.AccountId, false, false, reset: false);
        check(accounts.Statistics(b.AccountId) is null, "disabling statistics clears retained totals");
        game.Detach(pb, intentional: true);
        check(pa.State.Players.Length == 1 && game.GetStatus(TimeSpan.Zero).ConnectedUsers == 2,
            "intentional window closure immediately removes room membership and connection counts");
        var reopened = new TestPeer(b.AccountId); game.Attach(reopened);
        check(reopened.RoomCode is null && Social(reopened).People.All(p => p.Relationship != "recent"),
            "reopening starts outside the room and clears session-only recent players");
        game.Handle(reopened, new("friend-remove", TargetId: a.AccountId));
        check(!accounts.Friends(a.AccountId, b.AccountId) && Social(pa).People.All(p => !p.Online),
            "removing friendship revokes presence for both parties");
        game.Handle(reopened, new("join", Text: code));
        game.Detach(reopened);
        var resumed = new TestPeer(b.AccountId); game.Attach(resumed);
        check(resumed.RoomCode == code, "unexpected network drops retain the bounded reconnect grace");
        game.Detach(reopened, intentional: true);
        check(resumed.RoomCode == code, "stale sockets cannot remove a newer connection's room membership");
        check(!Protocol.ValidEvent(new("social", Social: new([new(a.Profile, "unknown")], new(false, false), []))) &&
            !Protocol.ValidEvent(new("shared-profile", SharedProfile: new(a.Profile, true, new(0, 1, 0, 0)))),
            "social packets reject invalid relationship states and impossible lifetime totals");

        string migration = Path.Combine(working, "duplicate-name-import");
        Directory.CreateDirectory(migration);
        string[] oldNames = ["Duplicate Mog", "duplicate mog", "Duplicate Mog ~2"];
        var source = oldNames.Select((name, i) => new StoredAccount(new(Guid.NewGuid(), name, i),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(new string((char)('a' + i), 43)))),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("recovery-" + i))))).ToDictionary(a => a.Profile.Id);
        File.WriteAllBytes(Path.Combine(migration, "accounts.json"), JsonSerializer.SerializeToUtf8Bytes(source, Protocol.Json));
        using var migrated = new AccountStore(migration);
        check(migrated.Count() == 3 && migrated.Authenticate(new string('a', 43))?.DisplayName == "Duplicate Mog" &&
            migrated.Authenticate(new string('b', 43))?.DisplayName == "duplicate mog ~3" &&
            migrated.Authenticate(new string('c', 43))?.DisplayName == "Duplicate Mog ~2",
            "duplicate-name migration preserves sign-ins and reserves existing suffixed names");
        migrated.SetPreferences(source.Keys.First(), true, true, false);
        var winnerId = source.Keys.First();
        migrated.RecordMatch([new(winnerId, "Duplicate Mog", 1200), new(source.Keys.Last(), "Duplicate Mog ~2", 100)], [winnerId]);
        check(migrated.Statistics(winnerId) == new LifetimeStats(1, 1, 1200, 1200),
            "opted-in lifetime stats retain authoritative points, wins and best match");

        var shared = accounts.Create("Explicit shared setup", shareProfile: true);
        check(accounts.Preferences(shared.AccountId) == new ProfilePreferences(false, true, false),
            "explicit setup sharing never implicitly enables statistics or background checks");
        accounts.SetPreferences(b.AccountId, false, false, false, backgroundInvitations: true);
        check(accounts.Preferences(b.AccountId).BackgroundInvitations && !accounts.Preferences(a.AccountId).BackgroundInvitations,
            "closed-window invitation checks require an account's own explicit opt-in");
        // Re-establish a mutual friendship after the earlier removal test.
        game.Handle(pa, new("friend-request", TargetId: b.AccountId));
        game.Handle(resumed, new("friend-accept", TargetId: a.AccountId));
        game.Detach(resumed, intentional: true);
        game.Handle(pa, new("invite", TargetId: b.AccountId));
        check(pa.Events[^1].Type == "notice", "an opted-in offline friend can receive a bounded queued invitation");
        var backgroundInbox = game.InvitationInbox(b.AccessToken)!;
        check(backgroundInbox.Enabled && backgroundInbox.Invitations.Length == 1 &&
            backgroundInbox.Invitations[0].From.AvatarPixels is null && game.GetStatus(TimeSpan.Zero).ConnectedUsers == 2,
            "authenticated inbox polling returns compact metadata without attaching a game connection");
        rejected = false;
        try { game.InvitationInbox(b.AccessToken); } catch (InboxRateLimitException) { rejected = true; }
        check(rejected && game.InvitationInbox("invalid-token") is null,
            "inbox polling rejects invalid credentials and enforces per-account rate limits");
        clock.Advance(21);
        accounts.SetPreferences(b.AccountId, false, false, false, backgroundInvitations: false);
        check(game.InvitationInbox(b.AccessToken) is { Enabled: false, Invitations.Length: 0 },
            "opting out disables invitation polling without exposing queued invitations");
        clock.Advance(301); game.Tick();
        var returnPeer = new TestPeer(b.AccountId); game.Attach(returnPeer);
        check(Social(returnPeer).Invitations.Length == 0, "expired offline invitations are pruned before reconnect");

        string oldPreferences = Path.Combine(working, "schema-three-privacy");
        Guid privateId;
        using (var original = new AccountStore(oldPreferences))
        {
            privateId = original.Create("Private before upgrade").AccountId;
            original.SetPreferences(privateId, true, false, false);
        }
        using (var oldDatabase = new SqliteConnection("Data Source=" + Path.Combine(oldPreferences, "accounts.db")))
        {
            oldDatabase.Open();
            using var downgrade = oldDatabase.CreateCommand();
            downgrade.CommandText = "ALTER TABLE preferences DROP COLUMN invite_checks; PRAGMA user_version=3";
            downgrade.ExecuteNonQuery();
        }
        using var upgradedPreferences = new AccountStore(oldPreferences);
        check(upgradedPreferences.Preferences(privateId) == new ProfilePreferences(true, false, false),
            "schema upgrade preserves private profiles and prior stats consent while defaulting background checks off");
    }
}
