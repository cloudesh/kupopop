namespace Kupopop.Shared;

/// <summary>Named factories for commands whose legacy wire fields have contextual meanings.</summary>
public static class ClientCommands
{
    public static ClientCommand ProfilePrivacy(bool trackStats, bool shareProfile,
        bool? backgroundInvitations = null, bool resetStats = false) => new("profile-privacy",
            Enabled: trackStats, Shared: shareProfile, BackgroundInvitations: backgroundInvitations,
            ResetAvatar: resetStats); // Protocol 5 uses this field for the statistics reset action.
}
