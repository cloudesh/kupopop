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
        var panel = new Vector2(Math.Min(size.X - UiTheme.S(16), UiTheme.S(collapsed ? 94 : 340)), UiTheme.S(collapsed ? 40 : 76));
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
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, UiTheme.V(6, 6)))
        using (ImRaii.PushStyle(ImGuiStyleVar.ChildBorderSize, 0))
        using (ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, UiTheme.S(5)))
        using (var panel = ImRaii.Child("canvas-tools", rect.Max - rect.Min, true,
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.AlwaysUseWindowPadding))
        {
            if (panel.Success)
            {
                ImGui.AlignTextToFramePadding(); UiTheme.Caption("Tools");
                ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(), rect.Max.X - rect.Min.X - UiTheme.S(34)));
                if (ToolAction(collapsed ? FontAwesomeIcon.ChevronUp : FontAwesomeIcon.ChevronDown, collapsed ? "Expand tools" : "Collapse tools"))
                { plugin.Config.CanvasToolsCollapsed = !collapsed; plugin.Save(); }
                if (!collapsed) painting.Toolbar("game-tools", undo: () => { if (Send(new("undo", TurnId: state.TurnId))) undoPending = true; }, canUndo: canUndoDrawing && !undoPending);
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
        var available = new Vector2(painting.RenderSize.X, Math.Max(60, painting.RenderSize.Y - UiTheme.S(46)));
        var panelSize = new Vector2(Math.Max(60, available.X - UiTheme.S(28)),
            Math.Max(60, Math.Min(available.Y - UiTheme.S(28), UiTheme.S(state.WordChoices?.Length > 4 ? 370 : 250))));
        var origin = painting.RenderOrigin + new Vector2(0, UiTheme.S(46)) + (available - panelSize) / 2;
        ImGui.SetCursorScreenPos(origin);
        using (ImRaii.PushColor(ImGuiCol.ChildBg, UiPresentation.Alpha(UiTheme.Panel, 0.78f)))
        using (ImRaii.PushColor(ImGuiCol.Border, Vector4.Zero))
        using (ImRaii.PushStyle(ImGuiStyleVar.ChildBorderSize, 0))
        using (ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, UiTheme.S(8)))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, UiTheme.V(12, 12)))
        using (var panel = ImRaii.Child("word-choices", panelSize, true))
        {
            if (panel.Success)
            {
                art.CenteredHeading("Choose your word", ImGui.GetContentRegionAvail().X);
                if (state.WordChoices is { Length: > 0 } choices)
                {
                    if (choices.Length <= 4 && panelSize.Y >= UiTheme.S(230))
                        UiPresentation.CenteredLabel("Your drawing timer starts after you choose.", ImGui.GetContentRegionAvail().X, UiTheme.Muted);
                    int columns = choices.Length == 1 ? 1 : 2, rows = (choices.Length + columns - 1) / columns;
                    float rowSpacing = ImGui.GetStyle().ItemSpacing.Y + ImGui.GetStyle().CellPadding.Y * 2;
                    float cardHeight = Math.Clamp((ImGui.GetContentRegionAvail().Y - rowSpacing * rows - UiTheme.S(4)) / rows, UiTheme.S(32), UiTheme.S(66));
                    if (ImGui.BeginTable("word-offers", columns, ImGuiTableFlags.SizingStretchSame))
                    {
                        for (int i = 0; i < choices.Length; i++)
                        {
                            ImGui.TableNextColumn();
                            if (WordCard("word-" + i, choices[i], i, cardHeight)) Send(new("choose-word", Text: choices[i], TurnId: state.TurnId));
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
        var color = new[] { UiTheme.StickyYellow, UiTheme.StickyRose, UiTheme.StickySage, UiTheme.StickyBlue }[index % 4];
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(start, start + size, UiTheme.C(UiPresentation.Alpha(color, hovered ? 0.94f : 0.74f)), UiTheme.S(4));
        var textSize = ImGui.CalcTextSize(word, false, Math.Max(20, size.X - UiTheme.S(14)));
        ImGui.SetCursorScreenPos(start + new Vector2(Math.Max(UiTheme.S(7), (size.X - textSize.X) / 2), (size.Y - textSize.Y) / 2));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + size.X - UiTheme.S(14));
        ImGui.TextUnformatted(word); ImGui.PopTextWrapPos();
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

    private void DrawTurnReveal(RoomView state)
    {
        var saved = ImGui.GetCursorScreenPos();
        var origin = ImGui.GetWindowPos() + UiTheme.V(18, 8);
        var size = ImGui.GetWindowSize() - UiTheme.V(36, 16);
        ImGui.SetCursorScreenPos(origin);
        using (ImRaii.PushColor(ImGuiCol.ChildBg, Vector4.Zero))
        using (var shade = ImRaii.Child("turn-reveal-overlay", size, false, ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (shade.Success)
            {
                var draw = ImGui.GetWindowDrawList();
                draw.AddRectFilled(origin, origin + size, ImGui.GetColorU32(new Vector4(0.18f, 0.18f, 0.22f, 0.28f)));
                float cardWidth = Math.Min(UiTheme.S(520), size.X - UiTheme.S(36));
                float contentHeight = art.MeasureLarge(state.Word ?? "", cardWidth - UiTheme.S(36)).Y + UiTheme.S(84) +
                    (state.FirstGuessers is { Length: > 0 } ? UiTheme.S(122) : ImGui.GetTextLineHeightWithSpacing());
                var card = new Vector2(cardWidth, Math.Min(Math.Max(UiTheme.S(180), contentHeight), size.Y - UiTheme.S(40)));
                var position = origin + (size - card) / 2;
                draw.AddRectFilled(position, position + card, UiTheme.C(UiTheme.Panel), UiTheme.S(3));
                draw.AddRect(position, position + card, UiTheme.C(UiTheme.Brass), UiTheme.S(3));
                ImGui.SetCursorScreenPos(position + UiTheme.V(18, 16));
                ImGui.BeginGroup();
                art.Heading(string.IsNullOrEmpty(state.Word) ? "The turn ended" : "The word was...");
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + card.X - UiTheme.S(36));
                art.LargeText(state.Word ?? "", wrap: true);
                ImGui.PopTextWrapPos(); UiTheme.Gap(10);
                if (state.FirstGuessers is { Length: > 0 } winners)
                    UiPresentation.Podium(winners, card.X - UiTheme.S(36));
                else UiTheme.Caption("A fresh page is coming up.");
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
            float height = ImGui.CalcTextSize(toast.Message, false, width - UiTheme.S(24)).Y + UiTheme.S(44);
            ImGui.SetCursorScreenPos(window + new Vector2(size.X - width - UiTheme.S(24), size.Y - UiTheme.S(30) - height));
            using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, UiTheme.V(10, 7)))
            using (ImRaii.PushColor(ImGuiCol.ChildBg, UiTheme.Panel))
            using (var child = ImRaii.Child("toast-" + toast.Id, new Vector2(width, height), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            {
                if (child.Success)
                {
                    ImGui.TextColored(UiTheme.Rose, toast.Title);
                    ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(), width - UiTheme.S(35)));
                    if (ToolAction(FontAwesomeIcon.Times, "Dismiss notification"))
                    {
                        toasts.Dismiss(toast.Id);
                        if (toast.Id.StartsWith("update:", StringComparison.Ordinal))
                        { plugin.Config.DismissedUpdate = toast.Id[7..]; plugin.Save(); }
                    }
                    UiTheme.Wrapped(toast.Message);
                }
            }
            size.Y -= height + UiTheme.S(6);
        }
        ImGui.SetCursorScreenPos(saved);
    }
}
