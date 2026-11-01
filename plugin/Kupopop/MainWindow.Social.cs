using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Kupopop.Shared;

namespace Kupopop;

public sealed partial class MainWindow
{
    private SocialView? social;
    private SharedProfile? sharedProfile;
    private bool showSharedProfile, openSharedProfile, inviteAfterCreate, profileRequested;
    private int profileSettingsPage;

    private GameInvitation[] ActiveInvitations() => social?.Invitations.Where(i => i.ExpiresAt > DateTimeOffset.UtcNow).ToArray() ?? [];

    private bool HandleSocialEvent(ServerEvent message)
    {
        switch (message.Type)
        {
            case "social":
                social = message.Social;
                if (social is not null && plugin.Config.BackgroundInvitations != social.Preferences.BackgroundInvitations)
                { plugin.Config.BackgroundInvitations = social.Preferences.BackgroundInvitations; plugin.Save(); }
                return true;
            case "leaderboard":
                leaderboard = message.Leaderboard;
                if (leaderboard is not null) leaderboardMetric = leaderboard.Metric;
                return true;
            case "friend-presence":
                if (social is not null) social = social with
                {
                    People = social.People.Select(p =>
                    p.Relationship == "friend" && p.Profile.Id == message.ActorId ? p with { Online = message.Online } : p).ToArray()
                };
                return true;
            case "shared-profile": sharedProfile = message.SharedProfile; openSharedProfile = profileRequested; profileRequested = false; return true;
            case "invitation" when message.Invitation is { } invitation:
                toasts.Show("invite:" + invitation.Id, "A game invitation", invitation.From.DisplayName + " invited you to " + invitation.RoomTitle + ". Open Friends or Play to join.");
                return true;
            case "notice": NotifyStatus(message.Error ?? "Done."); return true;
            default: return false;
        }
    }

    private void OpenFriends(bool inviting = false)
    {
        if (inviting) friendsTab = "friend";
        ShowDebug = false; screen = "friends"; Send(new("social"));
    }

    private void SocialActions(Guid id)
    {
        if (ImGui.MenuItem("View profile")) ViewArtistProfile(id);
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

    private string friendsTab = "friend";

    private void FriendsScreen()
    {
        if (PageHeading("Friends")) return;
        var tabs = ImGui.GetCursorScreenPos();
        foreach (var tab in new[] { ("incoming", "Requests"), ("friend", "Friends"), ("outgoing", "Sent"), ("recent", "Recently played") })
        {
            if (tab.Item1 != "incoming") ImGui.SameLine(0, UiTheme.S(3));
            int count = social?.People.Count(p => p.Relationship == tab.Item1) ?? 0;
            if (UiTheme.PanelTab(tab.Item2 + "  " + count, friendsTab == tab.Item1)) friendsTab = tab.Item1;
        }
        ImGui.SetCursorScreenPos(tabs + UiTheme.V(0, 34));
        using var panel = new UiTheme.PaperPanel("friends-list", Vector2.Zero, attachedTabs: true);
        if (!panel.Success) return;
        foreach (var invite in ActiveInvitations()) InvitationCard(invite);
        if (ImGui.BeginTable("friends-tools", 2, ImGuiTableFlags.NoPadOuterX))
        {
            ImGui.TableSetupColumn("Explanation", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Refresh", ImGuiTableColumnFlags.WidthFixed, UiTheme.S(80));
            ImGui.TableNextColumn();
            UiTheme.Wrapped(friendsTab switch
            {
                "incoming" => "A new face for your sketchbook. Accept to become friends.",
                "outgoing" => "Your invitations to become friends, waiting for a reply.",
                "recent" => "Artists from this session. This list clears when you close the book.",
                _ => "A little company makes every drawing better.",
            }, UiTheme.Muted);
            ImGui.TableNextColumn();
            if (UiTheme.Button("Refresh", 0, quiet: true)) Send(new("social"));
            ImGui.EndTable();
        }
        UiTheme.Gap(8);
        var people = social?.People.Where(p => p.Relationship == friendsTab).OrderByDescending(p => p.Online)
            .ThenBy(p => p.Profile.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        if (people.Length == 0)
        {
            UiTheme.Gap(28);
            UiTheme.Wrapped(friendsTab == "friend" ? "Your next drawing partner is a right-click away." : "Nothing on this page yet.");
            UiTheme.Wrapped(friendsTab == "friend" ? "Right-click an artist in a room and choose Add friend." : "Come back after your next game.", UiTheme.Muted);
        }
        foreach (var person in people) FriendCard(person);
    }

    private void FriendCard(SocialPerson person)
    {
        using var id = ImRaii.PushId(person.Profile.Id.ToString());
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, UiTheme.V(12, 10));
        using var background = ImRaii.PushColor(ImGuiCol.ChildBg, UiPresentation.Alpha(UiTheme.Inset, UiTheme.DarkMode ? 0.48f : 0.30f));
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, UiTheme.S(7));
        using var card = ImRaii.Child("friend-card", new Vector2(0, UiTheme.S(80)), false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (!card.Success) return;
        ImGui.SetCursorPos(UiTheme.V(12, 10));
        if (!ImGui.BeginTable("friend-row", 3, ImGuiTableFlags.NoPadOuterX, new Vector2(ImGui.GetContentRegionAvail().X - UiTheme.S(12), 0))) return;
        try
        {
            ImGui.TableSetupColumn("Portrait", ImGuiTableColumnFlags.WidthFixed, UiTheme.S(64));
            ImGui.TableSetupColumn("Artist", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, UiTheme.S(170));
            ImGui.TableNextColumn();
            UiPortrait.Draw(avatars.Get(person.Profile).Handle, UiTheme.V(52, 52));
            ImGui.TableNextColumn();
            UiTheme.Gap(3);
            UiTheme.Ellipsis(person.Profile.DisplayName, ImGui.GetContentRegionAvail().X);
            string status = person.Relationship switch
            {
                "incoming" => "Would like to be your friend",
                "outgoing" => "Waiting for their reply",
                "recent" => "Shared a drawing room",
                _ => person.Online ? "Book open" : person.AcceptsOfflineInvites ? "Offline · accepts invitations" : "Offline",
            };
            ImGui.TextColored(person.Online ? UiTheme.Green : UiTheme.Muted, status);
            ImGui.TableNextColumn();
            UiTheme.Gap(8);
            if (UiTheme.Button("Profile", UiTheme.S(66), quiet: true)) ViewArtistProfile(person.Profile.Id);
            ImGui.SameLine(0, UiTheme.S(4));
            string action = person.Relationship switch { "incoming" => "Accept", "friend" => "Invite", "recent" => "Add", _ => "Cancel" };
            bool invite = person.Relationship == "friend";
            using (ImRaii.Disabled(invite && ((!person.Online && !person.AcceptsOfflineInvites) || room is null || room.HostId != device?.AccountId || room.Phase is not ("waiting" or "results"))))
                if (UiTheme.Button(action, UiTheme.S(62), primary: person.Relationship is "incoming" or "recent"))
                    Send(new(person.Relationship switch { "incoming" => "friend-accept", "friend" => "invite", "recent" => "friend-request", _ => "friend-remove" }, TargetId: person.Profile.Id));
            ImGui.SameLine(0, UiTheme.S(2));
            if (UiTheme.GlyphButton("Friend actions", "...", UiTheme.V(24, 28))) ImGui.OpenPopup("friend-options");
            if (ImGui.BeginPopup("friend-options")) { SocialActions(person.Profile.Id); ImGui.EndPopup(); }
        }
        finally { ImGui.EndTable(); }
    }

    private void InvitationCard(GameInvitation invitation)
    {
        using var id = ImRaii.PushId(invitation.Id.ToString());
        using var tint = ImRaii.PushColor(ImGuiCol.ChildBg, UiTheme.RoseTint);
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, UiTheme.V(12, 10));
        using var card = ImRaii.Child("invitation-card", new Vector2(0, UiTheme.S(94)), false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (!card.Success) return;
        ImGui.SetCursorPos(UiTheme.V(12, 10));
        ImGui.TextColored(UiTheme.Rose, "YOU'RE INVITED");
        UiTheme.Ellipsis(invitation.From.DisplayName + " · " + invitation.RoomTitle, ImGui.GetContentRegionAvail().X);
        using (ImRaii.Disabled(room is not null))
            if (UiTheme.Button("Join game", UiTheme.S(110), primary: true)) Send(new("accept-invite", TargetId: invitation.Id));
        ImGui.SameLine(); if (UiTheme.Button("Dismiss", 0, quiet: true)) Send(new("dismiss-invite", TargetId: invitation.Id));
    }

    private void ProfilePrivacyControls()
    {
        if (social is null) return;
        bool track = social.Preferences.TrackStats, share = social.Preferences.ShareProfile;
        bool background = social.Preferences.BackgroundInvitations;
        UiTheme.Caption("YOUR PRIVACY");
        if (UiTheme.Button("Enable all three", 0, primary: true)) Send(new("profile-privacy", Enabled: true, Shared: true, BackgroundInvitations: true));
        bool changed = ImGui.Checkbox("Record my lifetime game stats", ref track);
        changed |= ImGui.Checkbox("Share my profile", ref share);
        changed |= ImGui.Checkbox("Invitations while my book is closed", ref background);
        UiTheme.Wrapped("Optional: checks for friend invitations once a minute over HTTPS, without staying in a game session. Shows a Dalamud notification; it never opens your book automatically.", UiTheme.Muted);
        UiTheme.Wrapped("Profile sharing reveals plugin use to friends and room partners. Recorded statistics appear anonymously with your portrait on leaderboards unless you opt out below. Turning off statistics deletes your totals and likes; match scoring stays the same.", UiTheme.Muted);
        if (changed) Send(new("profile-privacy", Enabled: track, Shared: share, BackgroundInvitations: background));
        if (leaderboard is null)
        {
            if (UiTheme.Button("Leaderboard visibility", 0, quiet: true)) RequestLeaderboard();
        }
        else LeaderboardPrivacyControls();
        if (UiTheme.Button("View my profile", 0, quiet: true)) ViewArtistProfile(device!.AccountId);
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

    private void ViewArtistProfile(Guid id)
    {
        profileRequested = true;
        if (!Send(new("view-profile", TargetId: id))) profileRequested = false;
    }

    private void SharedProfilePopup()
    {
        if (openSharedProfile) { ImGui.OpenPopup("Artist profile"); showSharedProfile = true; openSharedProfile = false; }
        ImGui.SetNextWindowSize(UiTheme.V(470, 450), ImGuiCond.Appearing);
        ImGui.SetNextWindowPos(ImGui.GetWindowPos() + ImGui.GetWindowSize() / 2, ImGuiCond.Appearing, new Vector2(0.5f));
        using var surface = ImRaii.PushColor(ImGuiCol.PopupBg, UiTheme.Panel);
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, UiTheme.V(22, 18));
        if (!ImGui.BeginPopupModal("Artist profile", ref showSharedProfile, ImGuiWindowFlags.NoResize)) return;
        try
        {
            if (sharedProfile is not { } view) return;
            UiPortrait.Draw(avatars.Get(view.Profile).Handle, UiTheme.V(76, 76));
            ImGui.SameLine(0, UiTheme.S(16)); ImGui.BeginGroup();
            UiTheme.Gap(5);
            UiTheme.Ellipsis(view.Profile.DisplayName, Math.Max(UiTheme.S(100), ImGui.GetContentRegionAvail().X));
            ImGui.TextColored(view.Visible ? UiTheme.Green : UiTheme.Muted, view.Visible ? "A SHARED SKETCHBOOK" : "A PRIVATE SKETCHBOOK");
            UiTheme.Caption("Kupo Skribbl artist");
            ImGui.EndGroup();
            UiTheme.Gap(14);
            if (!view.Visible)
            {
                UiTheme.Wrapped("This artist is keeping their sketchbook to themselves.");
                UiTheme.Wrapped("Their lifetime statistics are private.", UiTheme.Muted);
            }
            else if (view.Stats is { } stats)
            {
                if (ImGui.BeginTable("lifetime-cards", 2, ImGuiTableFlags.SizingStretchSame))
                {
                    foreach (var metric in new[] { ("GAMES PLAYED", stats.Games.ToString("N0")), ("VICTORIES", stats.Wins.ToString("N0")), ("LIFETIME POINTS", stats.Points.ToString("N0")), ("BEST MATCH", stats.BestScore.ToString("N0")) })
                    {
                        ImGui.TableNextColumn();
                        var at = ImGui.GetCursorScreenPos();
                        var size = new Vector2(ImGui.GetContentRegionAvail().X, UiTheme.S(78));
                        ImGui.GetWindowDrawList().AddRectFilled(at, at + size, UiTheme.C(UiPresentation.Alpha(UiTheme.Inset, 0.55f)), UiTheme.S(6));
                        ImGui.SetCursorScreenPos(at + UiTheme.V(12, 9)); UiTheme.Caption(metric.Item1);
                        ImGui.SetCursorScreenPos(at + UiTheme.V(12, 30)); art.Heading(metric.Item2);
                        ImGui.SetCursorScreenPos(at); ImGui.Dummy(size);
                    }
                    ImGui.EndTable();
                }
                UiTheme.Gap(8);
                UiTheme.Wrapped("Completed matches since opting in. Victories include ties. Points are a personal record, not currency.", UiTheme.Muted);
            }
            else
            {
                UiTheme.Wrapped("A fresh page, with stories still to draw.");
                UiTheme.Wrapped("This artist hasn't enabled lifetime statistics.", UiTheme.Muted);
            }
            UiTheme.Gap(10);
            if (UiTheme.Button("Close sketchbook", ImGui.GetContentRegionAvail().X, primary: true)) { showSharedProfile = false; ImGui.CloseCurrentPopup(); }
        }
        finally { ImGui.EndPopup(); }
    }
}
