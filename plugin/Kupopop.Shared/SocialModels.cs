namespace Kupopop.Shared;

public sealed record SocialPerson(PlayerProfile Profile, string Relationship, bool Online = false, bool AcceptsOfflineInvites = false);
public sealed record ProfilePreferences(bool TrackStats, bool ShareProfile, bool BackgroundInvitations = false);
public sealed record InvitationInbox(bool Enabled, GameInvitation[] Invitations);
public sealed record LifetimeStats(long Games, long Wins, long Points, int BestScore);
public sealed record SharedProfile(PlayerProfile Profile, bool Visible, LifetimeStats? Stats,
    ProfilePreferences? Preferences = null);
public sealed record GameInvitation(Guid Id, PlayerProfile From, string RoomTitle, DateTimeOffset ExpiresAt);
public sealed record SocialView(SocialPerson[] People, ProfilePreferences Preferences, GameInvitation[] Invitations);

public static class SocialValidation
{
    public static bool Valid(ServerEvent message)
    {
        if (message.Leaderboard is { } board &&
            (board.Page is < 0 or > 199 || board.Metric is not ("best" or "points" or "games" or "likes") ||
            board.Entries is not { Length: <= 50 } || board.Entries.Any(e => e is null || !Protocol.ValidName(e.Name) ||
                !Protocol.ValidAvatar(e.AvatarPixels) || e.AvatarPreset is not null && AvatarPresets.Find(e.AvatarPreset) is null ||
                e.BestScore is < 0 or > 1_000_000 || e.Points < 0 || e.Games < 0 || e.Likes < 0))) return false;
        static bool Invite(GameInvitation i) => i is not null && i.Id != Guid.Empty && Protocol.ValidProfile(i.From)
            && Protocol.ValidName(i.RoomTitle);
        if (message.Invitation is { } invitation && !Invite(invitation)) return false;
        if (message.SharedProfile is { } profile && (!Protocol.ValidProfile(profile.Profile) ||
            profile.Stats is { } stats && (stats.Games < 0 || stats.Wins < 0 || stats.Wins > stats.Games ||
                stats.Points < 0 || stats.BestScore is < 0 or > 1_000_000))) return false;
        return message.Social is not { } social || (social.Preferences is not null &&
            social.People is { Length: <= 250 } && social.People.All(p => p is not null && Protocol.ValidProfile(p.Profile)
                && p.Relationship is "friend" or "incoming" or "outgoing" or "recent") &&
            social.People.Select(p => p.Profile.Id).Distinct().Count() == social.People.Length &&
            social.Invitations is { Length: <= 20 } && social.Invitations.All(Invite));
    }
}
