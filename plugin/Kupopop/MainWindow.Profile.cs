using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Kupopop.Shared;

namespace Kupopop;

public sealed partial class MainWindow
{
    private bool editingAvatar;
    private SharedProfile? ownProfile;

    private void ResetAvatarDraft()
    {
        if (profile is null) return;
        avatar.Load(AvatarPresets.Pixels(profile));
        avatarPreset = profile.AvatarPreset;
        avatarReset = profile.AvatarPixels is null && avatarPreset is null;
        avatarHistory.Clear();
    }

    private void RequestOwnProfile()
    {
        profileRequested = false;
        if (device is not null) Send(new("view-profile", TargetId: device.AccountId));
    }

    private void DrawProfilePreview()
    {
        if (profile is null) return;
        float width = ImGui.GetContentRegionAvail().X;
        float portraitSize = Math.Min(UiTheme.S(112), Math.Max(UiTheme.S(72), ImGui.GetContentRegionAvail().Y * 0.26f));
        var start = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(start + new Vector2(Math.Max(0, (width - portraitSize) / 2), UiTheme.S(8)));
        UiPortrait.Draw(avatars.Get(profile).Handle, new Vector2(portraitSize));
        ImGui.SetCursorScreenPos(new Vector2(start.X, ImGui.GetCursorScreenPos().Y));
        UiTheme.Gap(6);
        art.CenteredHeading(profile.DisplayName, width);
        UiTheme.Gap(6);
        float buttonWidth = ImGui.CalcTextSize("Change profile picture").X + UiTheme.S(24);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (width - buttonWidth) / 2));
        using (ImRaii.Disabled(savingProfile))
            if (UiTheme.Button("Change profile picture", buttonWidth, primary: true)) { ResetAvatarDraft(); editingAvatar = true; }
        ImGui.SetCursorScreenPos(new Vector2(start.X, ImGui.GetCursorScreenPos().Y));
        UiTheme.Gap(10);
        UiTheme.Wrapped(social?.Preferences.ShareProfile == true ? "Shared with friends and room partners." : "Your profile is private.", UiTheme.Muted);
        UiTheme.Gap(8);
        if (ownProfile?.Stats is { } stats) LifetimeStatCards(stats, compact: true);
        else if (ownProfile is null) UiTheme.Caption("Loading your game record...");
        else UiTheme.Wrapped("Lifetime statistics are switched off. You can enable them under Privacy & invitations.", UiTheme.Muted);
    }

    private void LifetimeStatCards(LifetimeStats stats, bool compact = false)
    {
        if (!ImGui.BeginTable("lifetime-cards", 2, ImGuiTableFlags.SizingStretchSame)) return;
        try
        {
            foreach (var metric in new[] { ("GAMES PLAYED", stats.Games.ToString("N0")), ("VICTORIES", stats.Wins.ToString("N0")), ("LIFETIME POINTS", stats.Points.ToString("N0")), ("BEST MATCH", stats.BestScore.ToString("N0")) })
            {
                ImGui.TableNextColumn();
                var at = ImGui.GetCursorScreenPos();
                var size = new Vector2(ImGui.GetContentRegionAvail().X, UiTheme.S(compact ? 64 : 78));
                ImGui.GetWindowDrawList().AddRectFilled(at, at + size, UiTheme.C(UiPresentation.Alpha(UiTheme.Inset, 0.55f)), UiTheme.S(6));
                ImGui.SetCursorScreenPos(at + UiTheme.V(12, 7)); UiTheme.Caption(metric.Item1);
                ImGui.SetCursorScreenPos(at + UiTheme.V(12, 26)); art.Heading(metric.Item2);
                ImGui.SetCursorScreenPos(at); ImGui.Dummy(size);
            }
        }
        finally { ImGui.EndTable(); }
    }
}
