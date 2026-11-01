using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Kupopop.Shared;

namespace Kupopop;

public sealed partial class MainWindow
{
    private SocialView? social;
    private SharedProfile? sharedProfile;
    private bool showSharedProfile, openSharedProfile, inviteAfterCreate;

    private GameInvitation[] ActiveInvitations() => social?.Invitations.Where(i => i.ExpiresAt > DateTimeOffset.UtcNow).ToArray() ?? [];

    private bool HandleSocialEvent(ServerEvent message)
    {
        switch (message.Type)
        {
            case "social": social = message.Social; return true;
            case "friend-presence":
                if (social is not null) social = social with
                {
                    People = social.People.Select(p =>
                    p.Relationship == "friend" && p.Profile.Id == message.ActorId ? p with { Online = message.Online } : p).ToArray()
                };
                return true;
            case "shared-profile": sharedProfile = message.SharedProfile; openSharedProfile = true; return true;
            case "invitation" when message.Invitation is { } invitation:
                toasts.Show("invite:" + invitation.Id, "A game invitation", invitation.From.DisplayName + " invited you to " + invitation.RoomTitle + ". Open Friends or Play to join.");
                return true;
            case "notice": NotifyStatus(message.Error ?? "Done."); return true;
            default: return false;
        }
    }

    private void OpenFriends()
    {
        ShowDebug = false; screen = "friends"; Send(new("social"));
    }

    private void SocialActions(Guid id)
    {
        if (ImGui.MenuItem("View profile")) Send(new("view-profile", TargetId: id));
        string? relationship = social?.People.FirstOrDefault(p => p.Profile.Id == id)?.Relationship;
        if (relationship is null or "recent")
        {
            if (ImGui.MenuItem("Add friend")) Send(new("friend-request", TargetId: id));
        }
        else if (relationship == "incoming")
        {
            if (ImGui.MenuItem("Accept friend request")) Send(new("friend-accept", TargetId: id));
            if (ImGui.MenuItem("Decline request")) Send(new("friend-remove", TargetId: id));
        }
        else if (ImGui.MenuItem(relationship == "friend" ? "Remove friend" : "Cancel friend request")) Send(new("friend-remove", TargetId: id));
    }

    private void FriendsScreen()
    {
        if (PageHeading("Friends", refresh: false)) return;
        if (UiTheme.Button("Refresh", 0, quiet: true)) Send(new("social"));
        UiTheme.Wrapped("Friend requests require acceptance. Accepted friends can see when your book is open and send in-plugin invitations. Recent players are private to this session and clear when you close the book.", UiTheme.Muted);
        using var panel = new UiTheme.PaperPanel("friends-list", Vector2.Zero);
        if (!panel.Success) return;
        foreach (var invite in ActiveInvitations())
        {
            using var id = ImRaii.PushId(invite.Id.ToString());
            UiTheme.Wrapped(invite.From.DisplayName + " invited you to " + invite.RoomTitle);
            using (ImRaii.Disabled(room is not null))
                if (UiTheme.Button("Join game", 0, primary: true)) Send(new("accept-invite", TargetId: invite.Id));
            ImGui.SameLine(); if (UiTheme.Button("Dismiss", 0, quiet: true)) Send(new("dismiss-invite", TargetId: invite.Id));
            if (room is not null) UiTheme.Caption("Leave your current room to accept this invitation.");
            UiTheme.Rule();
        }
        foreach (var group in new[] { ("incoming", "FRIEND REQUESTS"), ("friend", "FRIENDS"), ("outgoing", "SENT REQUESTS"), ("recent", "RECENTLY PLAYED WITH") })
        {
            UiTheme.Caption(group.Item2);
            var people = social?.People.Where(p => p.Relationship == group.Item1).ToArray() ?? [];
            if (people.Length == 0) UiTheme.Caption(group.Item1 == "friend" ? "Right-click an artist in your room to add them." : "None yet.");
            foreach (var person in people)
            {
                using var id = ImRaii.PushId(person.Profile.Id.ToString());
                ImGui.Image(avatars.Get(person.Profile).Handle, UiTheme.V(36, 36));
                ImGui.SameLine(); ImGui.BeginGroup();
                ImGui.TextUnformatted(person.Profile.DisplayName);
                if (person.Relationship == "friend") UiTheme.Caption(person.Online ? "Book open" : "Offline");
                ImGui.EndGroup();
                ImGui.SameLine();
                if (UiTheme.Button("Profile", 0, quiet: true)) Send(new("view-profile", TargetId: person.Profile.Id));
                ImGui.SameLine();
                if (person.Relationship == "incoming")
                {
                    if (UiTheme.Button("Accept")) Send(new("friend-accept", TargetId: person.Profile.Id));
                    ImGui.SameLine(); if (UiTheme.Button("Decline", 0, quiet: true)) Send(new("friend-remove", TargetId: person.Profile.Id));
                }
                else if (person.Relationship == "recent")
                {
                    if (UiTheme.Button("Add friend")) Send(new("friend-request", TargetId: person.Profile.Id));
                }
                else if (person.Relationship == "friend")
                {
                    using (ImRaii.Disabled(!person.Online || room is null || room.HostId != device?.AccountId || room.Phase is not ("waiting" or "results")))
                        if (UiTheme.Button("Invite")) Send(new("invite", TargetId: person.Profile.Id));
                    ImGui.SameLine(); if (UiTheme.Button("Remove", 0, quiet: true)) Send(new("friend-remove", TargetId: person.Profile.Id));
                }
                else if (UiTheme.Button("Cancel request", 0, quiet: true)) Send(new("friend-remove", TargetId: person.Profile.Id));
                UiTheme.Gap(4);
            }
            UiTheme.Rule();
        }
    }

    private void ProfilePrivacyControls()
    {
        if (social is null) return;
        bool track = social.Preferences.TrackStats, share = social.Preferences.ShareProfile;
        UiTheme.Rule();
        bool changed = ImGui.Checkbox("Record my lifetime game stats", ref track);
        changed |= ImGui.Checkbox("Share my profile", ref share);
        UiTheme.Wrapped("Sharing lets room partners and friends view your avatar and opted-in game totals. It reveals that you use this plugin. There is no global player directory. Turning off stats deletes your totals; it never changes match scores or awards currency.", UiTheme.Muted);
        if (changed) Send(new("profile-privacy", Enabled: track, Shared: share));
        if (UiTheme.Button("View my profile", 0, quiet: true)) Send(new("view-profile", TargetId: device!.AccountId));
        ImGui.SameLine();
        if (UiTheme.Button("Reset lifetime stats", 0, quiet: true)) ImGui.OpenPopup("reset-lifetime");
        if (ImGui.BeginPopup("reset-lifetime"))
        {
            UiTheme.Wrapped("Permanently clear your recorded game totals?");
            if (UiTheme.Button("Reset totals")) { Send(new("profile-privacy", Enabled: track, Shared: share, ResetAvatar: true)); ImGui.CloseCurrentPopup(); }
            ImGui.SameLine(); if (UiTheme.Button("Cancel", 0, quiet: true)) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }

    private void SharedProfilePopup()
    {
        if (openSharedProfile) { ImGui.OpenPopup("Artist profile"); showSharedProfile = true; openSharedProfile = false; }
        ImGui.SetNextWindowSize(UiTheme.V(390, 300), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal("Artist profile", ref showSharedProfile, ImGuiWindowFlags.NoResize)) return;
        try
        {
            if (sharedProfile is not { } view) return;
            ImGui.Image(avatars.Get(view.Profile).Handle, UiTheme.V(64, 64));
            ImGui.SameLine(); ImGui.TextUnformatted(view.Profile.DisplayName);
            UiTheme.Rule();
            if (!view.Visible) UiTheme.Wrapped("This artist keeps their profile private.");
            else if (view.Stats is { } stats)
            {
                ImGui.TextUnformatted($"Games completed: {stats.Games:N0}\nWins (including ties): {stats.Wins:N0}\nLifetime points: {stats.Points:N0}\nBest match: {stats.BestScore:N0}");
                UiTheme.Wrapped("Totals cover completed matches since opting in. These are game statistics, not spendable Kupo-Points.", UiTheme.Muted);
            }
            else UiTheme.Wrapped("Lifetime statistics are not enabled for this artist.", UiTheme.Muted);
            if (UiTheme.Button("Close", 0, primary: true)) { showSharedProfile = false; ImGui.CloseCurrentPopup(); }
        }
        finally { ImGui.EndPopup(); }
    }
}
