using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Kupopop.Shared;

namespace Kupopop;

public sealed partial class MainWindow
{
    private void Preferences()
    {
        if (PageHeading("Your sketchbook"))
        {
            return;
        }

        UiTheme.Gap(10);
        using var card = new UiTheme.PaperPanel("sketchbook-preferences", Vector2.Zero);
        if (!card.Success)
        {
            return;
        }

        bool reduceMotion = plugin.Config.ReduceMotion;
        bool darkMode = plugin.Config.DarkMode;
        bool sounds = plugin.Config.GameSounds;
        if (ImGui.Checkbox("Game sounds", ref sounds))
        {
            plugin.Config.GameSounds = sounds;
            plugin.Save();
            if (!sounds)
            {
                audio.Stop();
            }
        }
        if (ImGui.Checkbox("Dark paper", ref darkMode))
        {
            plugin.Config.DarkMode = darkMode;
            plugin.Save();
        }
        if (ImGui.Checkbox("Reduce decorative motion", ref reduceMotion))
        {
            plugin.Config.ReduceMotion = reduceMotion;
            plugin.Save();
        }
        UiTheme.Caption("Keep the moogle pom still while moving the book.");
        UiTheme.Wrapped("The standard frame is visible only in this debug view. Otherwise, drag empty paper to move the book and use its corner to resize.", UiTheme.Muted);
        UiTheme.Rule();
        UiTheme.Caption("DIAGNOSTICS");
        ImGui.TextUnformatted($"Connection: {(client?.Connected == true ? "online" : "offline")}   /   Events received: {received}   /   Drawing messages: {draws}");
        UiTheme.Wrapped(log.FilePath, UiTheme.Muted);
        if (UiTheme.Button("Copy log path"))
        {
            ImGui.SetClipboardText(log.FilePath);
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(room is null || client?.Connected != true))
        {
            if (UiTheme.Button("Resync room", 0, quiet: true))
            {
                Send(new("sync"));
            }
        }

        UiTheme.Gap(5);
        UiTheme.Wrapped("Diagnostics exclude recovery codes, login credentials, answer words, chat text, and drawing contents.", UiTheme.Muted);
        if (UiTheme.Button(newRecovery.Length > 0 ? "Back" : screen == "welcome" ? "Back to welcome" : "Back to play", 0, quiet: true))
        {
            ShowDebug = false;
        }
    }
}
