using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Kupopop.Shared;

namespace Kupopop;

public sealed partial class MainWindow
{
    private void ConnectingScreen()
    {
        UiTheme.Gap(40);
        UiStationery.PageMark("opening your sketchbook");
        art.Heading("Trying to connect");
        UiTheme.Gap(10);
        UiTheme.Wrapped(connectionStatus, UiTheme.Muted);
        if (!busy && client is not null && connectionRetry.Attempts > 0)
        {
            int remaining = (int)Math.Max(1, (connectionRetry.NextAttempt - Environment.TickCount64 + 999) / 1000);
            UiTheme.Caption($"Next attempt in {remaining}s. You will continue automatically once connected.");
        }
    }

    private void Welcome()
    {
        var available = ImGui.GetContentRegionAvail();
        var size = new Vector2(Math.Min(available.X, UiTheme.S(480)), Math.Min(available.Y, UiTheme.S(returningArtist ? 470 : 350)));
        ImGui.SetCursorScreenPos(ImGui.GetCursorScreenPos() + Vector2.Max(Vector2.Zero, (available - size) / 2));
        using var panel = new UiTheme.PaperPanel("welcome-card", size);
        if (!panel.Success)
        {
            return;
        }

        UiTheme.Caption("Welcome, kupo.");
        UiTheme.Gap(6);
        using (ImRaii.Disabled(busy || client is null))
        {
            UiTheme.Field("Display name", "##new-name", ref displayName, 40, "Your character or artist name");

            UiTheme.Wrapped("Recommended: record lifetime stats, share your profile with room partners and friends, and check for invitations once a minute while closed. Recorded stats appear anonymously with your portrait on leaderboards unless you opt out. Sharing or a recognisable portrait can reveal plugin use. You can hide or disable each feature later.", UiTheme.Muted);
            using (ImRaii.Disabled(!Protocol.ValidName(displayName)))
            {
                if (UiTheme.Button(busy ? "Creating your profile..." : "Create & enable all three", ImGui.GetContentRegionAvail().X, primary: true))
                {
                    CreateAccount(false, enableAll: true);
                }

                if (UiTheme.Button("Create a private profile", ImGui.GetContentRegionAvail().X, quiet: true))
                {
                    CreateAccount(false);
                }
            }
            UiTheme.Gap(3);
            UiTheme.Wrapped("Your name is unverified. Players see your chosen name and avatar when you share a room.", UiTheme.Muted);
            UiTheme.Rule();
            if (UiTheme.Button(returningArtist ? "Hide account recovery" : "Already have a profile?", ImGui.GetContentRegionAvail().X, quiet: true))
            {
                returningArtist = !returningArtist;
            }

            if (returningArtist)
            {
                UiTheme.Field("Private recovery code", "##recover", ref recoveryInput, 128, "Paste your saved recovery code", ImGuiInputTextFlags.Password);
                if (UiTheme.Button("Recover my profile", ImGui.GetContentRegionAvail().X))
                {
                    CreateAccount(true);
                }
            }
        }
    }

    private void RecoveryScreen()
    {
        using var panel = new UiTheme.PaperPanel("save-recovery", Vector2.Zero);
        if (!panel.Success)
        {
            return;
        }

        UiTheme.Caption("YOUR PROFILE IS READY");
        art.Heading("Keep your sketchbook safe");
        UiTheme.Gap(8);
        UiTheme.Wrapped("Save this private recovery code in your password manager. It is the way back to your account if you lose access to this device.", UiTheme.Muted);
        UiTheme.Gap(12);
        ImGui.Checkbox("Show my private code", ref revealRecovery);
        UiTheme.Field("Recovery code", "##new-recovery", ref newRecovery, 128, "", ImGuiInputTextFlags.ReadOnly |
            (revealRecovery ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password));
        if (UiTheme.Button("Copy recovery code"))
        {
            ImGui.SetClipboardText(newRecovery);
        }

        UiTheme.Wrapped("Anyone with this code can recover your profile. It is not saved in the plugin configuration or diagnostic logs.", UiTheme.Muted);
        UiTheme.Rule();
        ImGui.Checkbox("I have saved my code somewhere private", ref savedRecovery);
        if (profile is not null)
        {
            UiPortrait.Draw(avatars.Get(profile).Handle, UiTheme.V(64, 64));
            ImGui.SameLine();
            ImGui.TextUnformatted("A little face to get you started.");
        }
        using (ImRaii.Disabled(!savedRecovery))
        {
            if (UiTheme.Button("Use this avatar & continue", 0, primary: true))
            {
                newRecovery = "";
                screen = room is null ? "menu" : "game";
            }
            ImGui.SameLine();
            if (UiTheme.Button("Draw my own"))
            {
                newRecovery = "";
                OpenProfile(editPicture: true);
            }
        }
    }
}
