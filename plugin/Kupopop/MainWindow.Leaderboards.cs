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
        if (leaderboard is null) return;
        UiTheme.Wrapped("Names are anonymous by default. Your portrait can identify you, including anything you draw on it. Only opted-in lifetime stats appear.", UiTheme.Muted);
        LeaderboardIdentityControls();
        LeaderboardOptOutButton();
    }

    private void LeaderboardIdentityControls(bool inline = false)
    {
        if (leaderboard is not { } board) return;
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, UiTheme.V(3, 2));
        bool portrait = board.ShowPortrait;
        if (ImGui.Checkbox("Show my portrait", ref portrait))
            Send(new("leaderboard-portrait", Enabled: portrait));
        if (inline) ImGui.SameLine(0, UiTheme.S(12));
        bool named = board.Named;
        if (ImGui.Checkbox("Include my name", ref named))
            Send(new("leaderboard-name", Enabled: named));
    }

    private void LeaderboardOptOutButton()
    {
        if (leaderboard is { } board && UiTheme.Button(board.Hidden ? "Rejoin leaderboards" : "Opt out of leaderboards", 0, quiet: true))
            Send(new("leaderboard-hidden", Enabled: !board.Hidden));
    }

    private void LeaderboardsScreen()
    {
        float rightEdge = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
        foreach (var (metric, label) in new[] { ("best", "Best game"), ("points", "Overall score"), ("games", "Most games"), ("likes", "Most likes") })
        {
            if (metric != "best") ImGui.SameLine(0, UiTheme.S(3));
            if (UiTheme.PanelTab(label, leaderboardMetric == metric))
            { leaderboardMetric = metric; RequestLeaderboard(); }
        }
        ImGui.SameLine();
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), rightEdge - ImGui.CalcTextSize("Refresh").X - UiTheme.S(16)));
        if (UiTheme.Button("Refresh", 0, quiet: true)) RequestLeaderboard(leaderboard?.Page ?? 0);
        if (leaderboard is not { } board) { UiTheme.Wrapped("Loading leaderboards...", UiTheme.Muted); return; }
        float rankingsHeight = Math.Max(UiTheme.S(80), ImGui.GetContentRegionAvail().Y - UiTheme.S(38));
        using (var panel = new UiTheme.PaperPanel("leaderboard-results", new Vector2(0, rankingsHeight), compact: true, attachedTabs: true))
        {
            if (panel.Success) DrawLeaderboardRows(board);
        }
        DrawLeaderboardFooter(board);
    }

    private void DrawLeaderboardRows(LeaderboardView board)
    {
        if (board.Entries.Length == 0) UiTheme.Wrapped("No scores recorded yet. Play a match to start your story.", UiTheme.Muted);
        if (!ImGui.BeginTable("rankings", 4, ImGuiTableFlags.NoPadOuterX | ImGuiTableFlags.RowBg)) return;
        try
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
        }
        finally { ImGui.EndTable(); }
    }

    private void DrawLeaderboardFooter(LeaderboardView board)
    {
        using var spacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, UiTheme.V(6, 3));
        if (!ImGui.BeginTable("leaderboard-footer", 3, ImGuiTableFlags.NoPadOuterX)) return;
        try
        {
            ImGui.TableSetupColumn("Pages", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("PreviousPage 200Next").X + UiTheme.S(48));
            ImGui.TableSetupColumn("Visibility", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Opt out", ImGuiTableColumnFlags.WidthFixed,
                ImGui.CalcTextSize(board.Hidden ? "Rejoin leaderboards" : "Opt out of leaderboards").X + UiTheme.S(16));
            ImGui.TableNextColumn();
            using (ImRaii.Disabled(board.Page == 0))
                if (UiTheme.Button("Previous", 0, quiet: true)) RequestLeaderboard(board.Page - 1);
            ImGui.SameLine(); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted($"Page {board.Page + 1}"); ImGui.SameLine();
            using (ImRaii.Disabled(!board.HasNext))
                if (UiTheme.Button("Next", 0, quiet: true)) RequestLeaderboard(board.Page + 1);
            ImGui.TableNextColumn();
            LeaderboardIdentityControls(inline: true);
            ImGui.TableNextColumn();
            LeaderboardOptOutButton();
        }
        finally { ImGui.EndTable(); }
    }
}
