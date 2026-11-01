using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Kupopop.Shared;

namespace Kupopop;

public sealed partial class MainWindow
{
    private (Vector2 Min, Vector2 Max) CanvasToolsRect(Vector2 origin, Vector2 size)
    {
        bool collapsed = plugin.Config.CanvasToolsCollapsed;
        var panel = new Vector2(Math.Min(size.X - UiTheme.S(16), UiTheme.S(collapsed ? 36 : 350)), UiTheme.S(36));
        var min = origin + new Vector2(UiTheme.S(8), size.Y - panel.Y - UiTheme.S(8));
        return (min, min + panel);
    }

    private void DrawCanvasTools(RoomView state)
    {
        var saved = ImGui.GetCursorScreenPos();
        var rect = CanvasToolsRect(painting.RenderOrigin, painting.RenderSize);
        bool collapsed = plugin.Config.CanvasToolsCollapsed;
        ImGui.SetCursorScreenPos(rect.Min);
        using (ImRaii.PushColor(ImGuiCol.ChildBg, UiPresentation.Alpha(UiTheme.Panel, UiTheme.DarkMode ? 0.88f : 0.82f)))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, UiTheme.V(4, 4)))
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, UiTheme.V(4, 2)))
        using (ImRaii.PushStyle(ImGuiStyleVar.ChildBorderSize, 0))
        using (ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, UiTheme.S(5)))
        using (var panel = ImRaii.Child("canvas-tools", rect.Max - rect.Min, true,
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.AlwaysUseWindowPadding))
        {
            if (panel.Success)
            {
                if (ToolAction(collapsed ? FontAwesomeIcon.ChevronRight : FontAwesomeIcon.ChevronLeft, collapsed ? "Expand tools" : "Collapse tools"))
                { plugin.Config.CanvasToolsCollapsed = !collapsed; plugin.Save(); }
                if (!collapsed)
                {
                    ImGui.SameLine();
                    painting.Toolbar("game-tools", undo: () => { if (Send(new("undo", TurnId: state.TurnId))) undoPending = true; }, canUndo: canUndoDrawing && !undoPending, compact: true);
                }
            }
        }
        ImGui.SetCursorScreenPos(saved);
    }

    private void DrawCanvasTimer(RoomView state)
    {
        int left = Math.Max(0, (int)Math.Ceiling((state.Deadline - (DateTimeOffset.UtcNow + serverOffset)).TotalSeconds));
        string text = left + "s";
        var size = art.MeasureLarge(text, painting.RenderSize.X);
        var position = painting.RenderOrigin + new Vector2(Math.Max(UiTheme.S(4), painting.RenderSize.X - size.X - UiTheme.S(10)), UiTheme.S(6));
        art.DrawLarge(text, position, UiPresentation.Alpha(left <= 15 && state.Phase == "drawing" ? UiTheme.CanvasAccent : UiTheme.CanvasInk, 0.62f));
    }

    private void DrawWordChoices(RoomView state)
    {
        var saved = ImGui.GetCursorScreenPos();
        if (customWordTurn != state.TurnId)
        { customWordTurn = state.TurnId; customWordInput = ""; enteringCustomWord = false; }
        var available = painting.RenderSize;
        int offerCount = (state.WordChoices?.Length ?? 0) + (state.CustomWordAvailable ? 1 : 0);
        float desiredHeight = enteringCustomWord ? 200 : 64 + ((offerCount + 1) / 2) * 60;
        var panelSize = new Vector2(Math.Max(60, available.X - UiTheme.S(32)),
            Math.Max(60, Math.Min(available.Y - UiTheme.S(32), UiTheme.S(desiredHeight))));
        var origin = painting.RenderOrigin + (available - panelSize) / 2;
        ImGui.SetCursorScreenPos(origin);
        using (ImRaii.PushColor(ImGuiCol.ChildBg, UiPresentation.Alpha(UiTheme.Panel, 0.78f)))
        using (ImRaii.PushColor(ImGuiCol.Border, Vector4.Zero))
        using (ImRaii.PushStyle(ImGuiStyleVar.ChildBorderSize, 0))
        using (ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, UiTheme.S(8)))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, UiTheme.V(12, 12)))
        using (var panel = ImRaii.Child("word-choices", panelSize, true,
            ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.NoMove))
        {
            if (panel.Success)
            {
                art.CenteredHeading("Choose your word", ImGui.GetContentRegionAvail().X);
                if (enteringCustomWord && state.CustomWordAvailable)
                {
                    UiTheme.Caption("RARE · A WORD OF YOUR OWN");
                    ImGui.SetNextItemWidth(-1);
                    bool submitted = ImGui.InputTextWithHint("##custom-word", "Something everyone can draw and guess", ref customWordInput, 48, ImGuiInputTextFlags.EnterReturnsTrue);
                    using (ImRaii.Disabled(string.IsNullOrWhiteSpace(customWordInput)))
                        if (UiTheme.Button("Draw this word", ImGui.GetContentRegionAvail().X, primary: true)) submitted = true;
                    if (submitted && !string.IsNullOrWhiteSpace(customWordInput)) Send(new("choose-custom-word", Text: customWordInput, TurnId: state.TurnId));
                    if (UiTheme.Button("Back to word choices", 0, quiet: true)) enteringCustomWord = false;
                }
                else if (state.WordChoices is { Length: > 0 } choices)
                {
                    int columns = offerCount == 1 ? 1 : 2, rows = (offerCount + columns - 1) / columns;
                    float rowSpacing = ImGui.GetStyle().ItemSpacing.Y + ImGui.GetStyle().CellPadding.Y * 2;
                    float cardHeight = Math.Clamp((ImGui.GetContentRegionAvail().Y - rowSpacing * rows - UiTheme.S(4)) / rows, UiTheme.S(32), UiTheme.S(66));
                    if (ImGui.BeginTable("word-offers", columns, ImGuiTableFlags.SizingStretchSame))
                    {
                        for (int i = 0; i < offerCount; i++)
                        {
                            ImGui.TableNextColumn();
                            if (i < choices.Length)
                            {
                                if (WordCard("word-" + i, choices[i], i, cardHeight)) Send(new("choose-word", Text: choices[i], TurnId: state.TurnId));
                            }
                            else if (WordCard("custom-word-offer", "Rare\nYour own word", 4, cardHeight)) enteringCustomWord = true;
                        }
                        ImGui.EndTable();
                    }
                }
            }
        }
        ImGui.SetCursorScreenPos(saved);
    }

    private static bool WordCard(string id, string word, int index, float height)
    {
        var start = ImGui.GetCursorScreenPos();
        float width = ImGui.GetContentRegionAvail().X;
        var size = new Vector2(width, Math.Max(height, ImGui.CalcTextSize(word, false, Math.Max(20, width - UiTheme.S(14))).Y + UiTheme.S(12)));
        bool clicked = ImGui.InvisibleButton(id, size);
        bool hovered = ImGui.IsItemHovered();
        var end = ImGui.GetCursorScreenPos();
        var color = index == 4 ? UiTheme.Tape : new[] { UiTheme.StickyYellow, UiTheme.StickyRose, UiTheme.StickySage, UiTheme.StickyBlue }[index % 4];
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(start, start + size, UiTheme.C(UiPresentation.Alpha(color, hovered ? 0.94f : 0.74f)), UiTheme.S(4));
        UiPresentation.DrawCenteredText(word, start + UiTheme.V(7, 2), size - UiTheme.V(14, 4));
        ImGui.SetCursorScreenPos(end);
        return clicked;
    }

    private void DrawCanvasVotes(RoomView state)
    {
        var saved = ImGui.GetCursorScreenPos();
        bool showKick = state.Options.VoteKickEnabled, showSkip = state.Options.VoteSkipEnabled;
        if (!showKick && !showSkip) return;
        string kick = state.KickVote is { } k ? $"Kick {k.Votes}/{k.Required}" : "Vote kick";
        string skip = state.SkipVote is { } s ? $"Skip {s.Votes}/{s.Required}" : "Vote skip";
        float width = (showKick ? ImGui.CalcTextSize(kick).X + UiTheme.S(16) : 0) +
            (showSkip ? ImGui.CalcTextSize(skip).X + UiTheme.S(16) : 0) + (showKick && showSkip ? UiTheme.S(6) : 0);
        ImGui.SetCursorScreenPos(painting.RenderOrigin + new Vector2(Math.Max(UiTheme.S(4), painting.RenderSize.X - width - UiTheme.S(4)), painting.RenderSize.Y - UiTheme.S(32)));
        if (showKick)
        {
            using (ImRaii.Disabled(!state.KickVoteAvailable || state.KickVote?.YouVoted == true))
                if (UiPresentation.SoftButton(kick)) Send(new("vote-kick", TurnId: state.TurnId));
            if (ImGui.IsItemHovered()) UiTheme.Tooltip("Vote to remove the drawer. Under 5: all other players; 5+: 70%, rounded up. Ballot lasts 30s.");
        }
        if (showKick && showSkip) ImGui.SameLine();
        if (showSkip)
        {
            using (ImRaii.Disabled(!state.SkipVoteAvailable || state.SkipVote?.YouVoted == true))
                if (UiPresentation.SoftButton(skip)) Send(new("vote-skip", TurnId: state.TurnId));
            if (ImGui.IsItemHovered()) UiTheme.Tooltip("Vote to skip this turn without removing its drawer. Same threshold as a kick vote.");
        }
        ImGui.SetCursorScreenPos(saved);
    }

    private static void DrawChatCard(ChatLine line) => UiPresentation.ChatCard(line);

    private void DrawTurnReveal(RoomView state) => DrawVictoryCard(state, match: false);

    private void DrawMatchResults(RoomView state) => DrawVictoryCard(state, match: true);

    private void DrawVictoryCard(RoomView state, bool match)
    {
        var saved = ImGui.GetCursorScreenPos();
        var origin = painting.RenderOrigin;
        var size = painting.RenderSize;
        ImGui.SetCursorScreenPos(origin);
        using (ImRaii.PushColor(ImGuiCol.ChildBg, Vector4.Zero))
        using (var shade = ImRaii.Child(match ? "match-victory-overlay" : "turn-reveal-overlay", size, false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | (match ? ImGuiWindowFlags.None : ImGuiWindowFlags.NoInputs)))
        {
            if (shade.Success)
            {
                var draw = ImGui.GetWindowDrawList();
                draw.AddRectFilled(origin, origin + size, ImGui.GetColorU32(new Vector4(0.18f, 0.18f, 0.22f, 0.28f)));
                var winners = match ? state.MatchResults ?? [] : state.FirstGuessers ?? [];
                float cardWidth = size.X - UiTheme.S(24);
                float wordHeight = match ? 0 : art.MeasureLarge(state.Word ?? "", cardWidth - UiTheme.S(28)).Y;
                float contentHeight = UiTheme.S(match ? 290 : winners.Length > 0 ? 236 : 110) + wordHeight;
                var card = new Vector2(cardWidth, Math.Min(contentHeight, size.Y - UiTheme.S(24)));
                var position = origin + (size - card) / 2;
                draw.AddRectFilled(position, position + card, UiTheme.C(UiTheme.Panel), UiTheme.S(3));
                draw.AddRect(position, position + card, UiTheme.C(UiTheme.Brass), UiTheme.S(3));
                ImGui.SetCursorScreenPos(position + UiTheme.V(14, 12));
                ImGui.BeginGroup();
                float inside = card.X - UiTheme.S(28);
                var contentOrigin = ImGui.GetCursorScreenPos();
                art.CenteredHeading(match ? "The final masterpiece" : "The word was...", inside);
                ImGui.SetCursorPosX(contentOrigin.X - ImGui.GetWindowPos().X);
                if (!match)
                {
                    var measured = art.MeasureLarge(state.Word ?? "", inside);
                    ImGui.SetCursorScreenPos(new Vector2(contentOrigin.X + Math.Max(0, (inside - measured.X) / 2), ImGui.GetCursorScreenPos().Y));
                    ImGui.PushTextWrapPos(contentOrigin.X - ImGui.GetWindowPos().X + inside);
                    art.LargeText(state.Word ?? "", wrap: true); ImGui.PopTextWrapPos();
                }
                else if (winners.Length > 0)
                {
                    bool tied = winners.Length > 1 && winners[1].Points == winners[0].Points;
                    string victory = tied ? "A shared victory!" : winners[0].Name + " wins!";
                    ImGui.SetCursorPosX(contentOrigin.X - ImGui.GetWindowPos().X);
                    UiPresentation.CenteredLabel(victory, inside, UiTheme.Rose);
                }
                UiTheme.Gap(6);
                ImGui.SetCursorPosX(contentOrigin.X - ImGui.GetWindowPos().X);
                if (winners.Length > 0) UiPresentation.Podium(winners.Take(3).ToArray(), inside, totals: match,
                    portrait: (winner, at, extent) =>
                    {
                        var player = state.Players.FirstOrDefault(p => p.Profile.Id == winner.PlayerId);
                        if (player is not null) UiPortrait.DrawAt(avatars.Get(player.Profile).Handle, at, new Vector2(extent));
                        else UiStationery.Moogle(at + new Vector2(extent / 2), extent * 0.35f);
                    });
                else UiPresentation.CenteredLabel("A fresh page is coming up.", inside, UiTheme.Muted);
                if (match)
                {
                    ImGui.SetCursorPosX(contentOrigin.X - ImGui.GetWindowPos().X);
                    if (UiTheme.Button("Back to the room", inside, quiet: true)) resultsDismissed = state.TurnId;
                }
                ImGui.EndGroup();
            }
        }
        ImGui.SetCursorScreenPos(saved);
    }

    private void DrawToasts()
    {
        var saved = ImGui.GetCursorScreenPos();
        Vector2 window = ImGui.GetWindowPos(), size = ImGui.GetWindowSize();
        float width = Math.Min(UiTheme.S(320), size.X - UiTheme.S(48));
        var messages = toasts.Visible();
        for (int i = messages.Length - 1; i >= 0; i--)
        {
            var toast = messages[i];
            bool titled = !string.IsNullOrEmpty(toast.Title);
            float height = ImGui.CalcTextSize(toast.Message, false, width - UiTheme.S(titled ? 24 : 50)).Y + UiTheme.S(titled ? 44 : 22);
            ImGui.SetCursorScreenPos(window + new Vector2(size.X - width - UiTheme.S(24), size.Y - UiTheme.S(30) - height));
            using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, UiTheme.V(10, 7)))
            using (ImRaii.PushColor(ImGuiCol.ChildBg, UiTheme.Panel))
            using (var child = ImRaii.Child("toast-" + toast.Id, new Vector2(width, height), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            {
                if (child.Success)
                {
                    var content = ImGui.GetCursorScreenPos();
                    if (titled) ImGui.TextColored(UiTheme.Rose, toast.Title);
                    ImGui.SetCursorScreenPos(new Vector2(content.X + width - UiTheme.S(48), content.Y));
                    if (ToolAction(FontAwesomeIcon.Times, "Dismiss notification"))
                    {
                        toasts.Dismiss(toast.Id);
                        if (toast.Id.StartsWith("update:", StringComparison.Ordinal))
                        { plugin.Config.DismissedUpdate = toast.Id[7..]; plugin.Save(); }
                    }
                    if (!titled) ImGui.SetCursorScreenPos(content);
                    ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - UiTheme.S(titled ? 24 : 50));
                    ImGui.TextUnformatted(toast.Message); ImGui.PopTextWrapPos();
                }
            }
            size.Y -= height + UiTheme.S(6);
        }
        ImGui.SetCursorScreenPos(saved);
    }
}
