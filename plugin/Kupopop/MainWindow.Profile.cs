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
        if (profile is null)
        {
            return;
        }

        avatar.Load(AvatarPresets.Pixels(profile));
        avatarPreset = profile.AvatarPreset;
        avatarReset = profile.AvatarPixels is null && avatarPreset is null;
        avatarHistory.Clear();
    }

    private void RequestOwnProfile()
    {
        profileRequested = false;
        if (device is not null)
        {
            Send(new("view-profile", TargetId: device.AccountId));
        }
    }

    private void DrawProfilePreview()
    {
        if (profile is null)
        {
            return;
        }

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
        {
            if (UiTheme.Button("Change profile picture", buttonWidth, primary: true))
            {
                ResetAvatarDraft();
                editingAvatar = true;
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(start.X, ImGui.GetCursorScreenPos().Y));
        UiTheme.Gap(10);
        UiTheme.Wrapped(social?.Preferences.ShareProfile == true ? "Shared with friends and room partners." : "Your profile is private.", UiTheme.Muted);
        UiTheme.Gap(8);
        if (ownProfile?.Stats is { } stats)
        {
            LifetimeStatCards(stats, compact: true);
        }
        else if (ownProfile is null)
        {
            UiTheme.Caption("Loading your game record...");
        }
        else
        {
            UiTheme.Wrapped("Lifetime statistics are switched off. You can enable them under Privacy & invitations.", UiTheme.Muted);
        }
    }

    private void LifetimeStatCards(LifetimeStats stats, bool compact = false)
    {
        if (!ImGui.BeginTable("lifetime-cards", 2, ImGuiTableFlags.SizingStretchSame))
        {
            return;
        }

        try
        {
            foreach (var metric in new[] { ("GAMES PLAYED", stats.Games.ToString("N0")), ("VICTORIES", stats.Wins.ToString("N0")), ("LIFETIME POINTS", stats.Points.ToString("N0")), ("BEST MATCH", stats.BestScore.ToString("N0")) })
            {
                ImGui.TableNextColumn();
                var at = ImGui.GetCursorScreenPos();
                var size = new Vector2(ImGui.GetContentRegionAvail().X, UiTheme.S(compact ? 64 : 78));
                ImGui.GetWindowDrawList().AddRectFilled(at, at + size, UiTheme.C(UiPresentation.Alpha(UiTheme.Inset, 0.55f)), UiTheme.S(6));
                ImGui.SetCursorScreenPos(at + UiTheme.V(12, 7));
                UiTheme.Caption(metric.Item1);
                ImGui.SetCursorScreenPos(at + UiTheme.V(12, 26));
                art.Heading(metric.Item2);
                ImGui.SetCursorScreenPos(at);
                ImGui.Dummy(size);
            }
        }
        finally { ImGui.EndTable(); }
    }

    private void OpenProfile(bool editPicture = false)
    {
        if (profile is null)
        {
            NotifyStatus("Connect to load your profile first.");
            return;
        }
        displayName = profile.DisplayName;
        ResetAvatarDraft();
        editingAvatar = editPicture;
        ownProfile = null;
        profileSettingsPage = 0;
        screen = "profile";
        RequestLeaderboard();
        RequestOwnProfile();
    }

    private void ProfileScreen()
    {
        if (!ImGui.BeginTable("profile-layout", 2))
        {
            return;
        }

        try
        {
            ImGui.TableSetupColumn("Portrait", ImGuiTableColumnFlags.WidthStretch, 1.15f);
            ImGui.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthStretch, 1);
            ImGui.TableNextColumn();
            using (var editor = new UiTheme.PaperPanel(editingAvatar ? "portrait-editor" : "profile-preview", Vector2.Zero))
            {
                if (editor.Success && !editingAvatar)
                {
                    DrawProfilePreview();
                }
                else if (editor.Success)
                {
                    UiTheme.Caption("YOUR CANVAS");
                    AvatarChoices();
                    avatarEditor.Toolbar("avatar-tools", transparency: true, undo: () => { avatarHistory.Undo(avatar); avatarReset = false; avatarPreset = null; }, canUndo: avatarHistory.CanUndo);
                    avatarEditor.Draw("avatar-canvas", avatar, new Vector2(ImGui.GetContentRegionAvail().X,
                        Math.Max(UiTheme.S(100), ImGui.GetContentRegionAvail().Y - UiTheme.S(38))), true,
                        operation => { avatarHistory.Capture(avatar, operation.StrokeId); avatar.Apply(operation); avatarReset = false; avatarPreset = null; });
                    UiTheme.Gap(5);
                    if (UiTheme.Button("Use my default avatar", 0, quiet: true) && profile is not null)
                    {
                        avatar.Load(AvatarGenerator.Generate(profile.AvatarSeed));
                        avatarReset = true;
                        avatarPreset = null;
                        avatarHistory.Clear();
                    }
                    ImGui.SameLine();
                    if (UiTheme.Button("Cancel picture changes", 0, quiet: true))
                    {
                        ResetAvatarDraft();
                        editingAvatar = false;
                    }
                }
            }
            ImGui.TableNextColumn();
            using var card = new UiTheme.PaperPanel("profile-details", Vector2.Zero, note: UiTheme.StickyRose);
            if (!card.Success)
            {
                return;
            }

            float backWidth = ImGui.CalcTextSize(room is null ? "Back to home" : "Back to room").X + UiTheme.S(36);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, ImGui.GetContentRegionAvail().X - backWidth));
            if (BackButton())
            {
                return;
            }

            UiTheme.Gap(4);
            if (UiTheme.Tab("Artist", profileSettingsPage == 0))
            {
                profileSettingsPage = 0;
            }

            ImGui.SameLine();
            if (UiTheme.Tab("Privacy & invitations", profileSettingsPage == 1))
            {
                profileSettingsPage = 1;
            }

            UiTheme.Gap(10);
            if (profileSettingsPage == 1)
            {
                ProfilePrivacyControls();
                UiTheme.Gap(8);
                UiTheme.Caption("Changes are saved automatically.");
                return;
            }
            UiTheme.Field("Display name", "##profile-name", ref displayName, 40, "Your artist name");
            UiTheme.Wrapped("Character unverified. Your name and avatar are shared with the people in your room.", UiTheme.Muted);
            UiTheme.Gap(6);
            using (ImRaii.Disabled(savingProfile || client?.Connected != true || !Protocol.ValidName(displayName)))
            {
                if (UiTheme.NoteButton(savingProfile ? "Saving..." : "Save my profile", ImGui.GetContentRegionAvail().X, UiTheme.StickyRose, primary: true))
                {
                    savingProfile = Send(new("profile", Text: displayName,
                        Avatar: !editingAvatar || avatarReset || avatarPreset is not null ? null : (byte[])avatar.Pixels.Clone(),
                        ResetAvatar: editingAvatar && avatarReset, AvatarPreset: editingAvatar ? avatarPreset : null));
                    if (savingProfile)
                    {
                        NotifyStatus("Saving profile...");
                    }
                }
            }

            UiTheme.Rule();
            UiTheme.Caption("ACCOUNT RECOVERY");
            UiTheme.Wrapped("Lost your saved code? Replace it while this device still has access. Your previous code stops working and other devices are signed out.", UiTheme.Muted);
            using (ImRaii.Disabled(busy || device is null || client?.Connected != true))
            {
                if (UiTheme.NoteButton("Replace recovery code", ImGui.GetContentRegionAvail().X, UiTheme.StickyRose))
                {
                    string token = device!.AccessToken;
                    Work(async (active, complete) => await AcceptCredentials(active, complete, await active.RotateRecoveryAsync(token)), "Replacing recovery code");
                }
            }
        }
        finally { ImGui.EndTable(); }
    }

    private void AvatarChoices()
    {
        ImGui.SetNextItemWidth(-1);
        if (!ImGui.BeginCombo("##avatar-presets", avatarPreset is { } id ? AvatarPresets.Find(id)?.Name ?? "Custom drawing" : avatarReset ? "Your default moogle" : "Custom drawing"))
        {
            return;
        }

        try
        {
            if (ImGui.Selectable("Your default moogle") && profile is not null)
            {
                avatar.Load(AvatarGenerator.Generate(profile.AvatarSeed));
                avatarReset = true;
                avatarPreset = null;
                avatarHistory.Clear();
            }
            if (ImGui.Selectable("New transparent drawing"))
            {
                avatar.Clear();
                avatarReset = false;
                avatarPreset = null;
                avatarHistory.Clear();
            }
            foreach (var preset in AvatarPresets.Items)
            {
                if (ImGui.Selectable(preset.Name, avatarPreset == preset.Id))
                {
                    avatar.Load(AvatarGenerator.Generate(preset.Seed));
                    avatarPreset = preset.Id;
                    avatarReset = false;
                    avatarHistory.Clear();
                }
            }
        }
        finally { ImGui.EndCombo(); }
    }
}
