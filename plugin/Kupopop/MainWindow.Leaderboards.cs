using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Kupopop.Shared;

namespace Kupopop;

public sealed partial class MainWindow
{
    private LeaderboardView? leaderboard;
    private string leaderboardMetric = "best";

    private void RequestLeaderboard(int page = 0) => Send(new("leaderboard", Text: leaderboardMetric, Page: page));

    private void LeaderboardPrivacyControls()
    {
        if (leaderboard is not { } board) return;
        UiTheme.Wrapped("Names are anonymous by default. Your portrait can identify you, including anything you draw on it. Only opted-in lifetime stats appear.", UiTheme.Muted);
        float controlsWidth = ImGui.GetContentRegionAvail().X;
        string nameAction = board.Named ? "Hide my name" : "Include my name";
        string visibilityAction = board.Hidden ? "Rejoin leaderboards" : "Opt out of leaderboards";
        if (UiTheme.Button(nameAction, 0, quiet: true))
            Send(new("leaderboard-name", Enabled: !board.Named));
        if (ImGui.CalcTextSize(nameAction).X + ImGui.CalcTextSize(visibilityAction).X + UiTheme.S(40) <= controlsWidth) ImGui.SameLine();
        if (UiTheme.Button(visibilityAction, 0, quiet: true))
            Send(new("leaderboard-hidden", Enabled: !board.Hidden));
        bool portrait = board.ShowPortrait;
        if (ImGui.Checkbox("Show my portrait on leaderboards", ref portrait))
            Send(new("leaderboard-portrait", Enabled: portrait));
    }

    private void LeaderboardsScreen()
    {
        if (PageHeading("Leaderboards")) return;
        LeaderboardPrivacyControls();
        UiTheme.Gap(5);
        foreach (var (metric, label) in new[] { ("best", "Best game"), ("points", "Overall score"), ("games", "Most games"), ("likes", "Most likes") })
        {
            if (metric != "best") ImGui.SameLine(0, UiTheme.S(3));
            if (UiTheme.PanelTab(label, leaderboardMetric == metric))
            { leaderboardMetric = metric; RequestLeaderboard(); }
        }
        ImGui.SameLine(); if (UiTheme.Button("Refresh", 0, quiet: true)) RequestLeaderboard(leaderboard?.Page ?? 0);
        if (leaderboard is not { } board) { UiTheme.Wrapped("Loading leaderboards...", UiTheme.Muted); return; }
        using var panel = new UiTheme.PaperPanel("leaderboard-results", Vector2.Zero, attachedTabs: true);
        if (!panel.Success) return;
        if (board.Entries.Length == 0) UiTheme.Wrapped("No scores recorded yet. Play a match to start your story.", UiTheme.Muted);
        if (ImGui.BeginTable("rankings", 4, ImGuiTableFlags.NoPadOuterX | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Rank", ImGuiTableColumnFlags.WidthFixed, UiTheme.S(40));
            ImGui.TableSetupColumn("Portrait", ImGuiTableColumnFlags.WidthFixed, UiTheme.S(42));
            ImGui.TableSetupColumn("Artist", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, UiTheme.S(110));
            for (int i = 0; i < board.Entries.Length; i++)
            {
                var entry = board.Entries[i];
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                ImGui.TextUnformatted((board.Page * 50 + i + 1).ToString()); ImGui.TableNextColumn();
                // Cache by page slot, never transmit an anonymous account ID.
                var bytes = new byte[16]; BitConverter.GetBytes(board.Page * 50 + i + 1).CopyTo(bytes, 0);
                var portrait = new PlayerProfile(new Guid(bytes), entry.Name, entry.AvatarSeed, entry.AvatarPixels, entry.AvatarPreset);
                UiPortrait.Draw(avatars.Get(portrait).Handle, UiTheme.V(32, 32)); ImGui.TableNextColumn();
                UiTheme.Ellipsis(entry.Name, ImGui.GetContentRegionAvail().X); ImGui.TableNextColumn();
                long total = board.Metric switch { "best" => entry.BestScore, "points" => entry.Points, "games" => entry.Games, _ => entry.Likes };
                ImGui.TextUnformatted(total.ToString("N0"));
            }
            ImGui.EndTable();
        }
        using (ImRaii.Disabled(board.Page == 0))
            if (UiTheme.Button("Previous", 0, quiet: true)) RequestLeaderboard(board.Page - 1);
        ImGui.SameLine(); ImGui.TextUnformatted($"Page {board.Page + 1}"); ImGui.SameLine();
        using (ImRaii.Disabled(!board.HasNext))
            if (UiTheme.Button("Next", 0, quiet: true)) RequestLeaderboard(board.Page + 1);
    }
}
