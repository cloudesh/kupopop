using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Kupopop.Shared;

namespace Kupopop;

public sealed partial class MainWindow
{
    private void DrawWordChoices(RoomView state)
    {
        var saved = ImGui.GetCursorScreenPos();
        var panelSize = new Vector2(Math.Max(60, painting.RenderSize.X - UiTheme.S(28)),
            Math.Max(60, Math.Min(painting.RenderSize.Y - UiTheme.S(28), UiTheme.S(state.WordChoices is null ? 145 : 250))));
        var origin = painting.RenderOrigin + (painting.RenderSize - panelSize) / 2;
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
                art.CenteredHeading(state.DrawerId == device?.AccountId ? "Choose your word" : "A little inspiration...", ImGui.GetContentRegionAvail().X);
                if (state.WordChoices is { Length: > 0 } choices)
                {
                    UiPresentation.CenteredLabel("Your drawing timer starts after you choose.", ImGui.GetContentRegionAvail().X, UiTheme.Muted);
                    if (ImGui.BeginTable("four-words", 2, ImGuiTableFlags.SizingStretchSame))
                    {
                        for (int i = 0; i < choices.Length; i++)
                        {
                            ImGui.TableNextColumn();
                            if (WordCard("word-" + i, choices[i], i)) Send(new("choose-word", Text: choices[i], TurnId: state.TurnId));
                        }
                        ImGui.EndTable();
                    }
                }
                else UiTheme.Wrapped((state.Players.FirstOrDefault(p => p.Profile.Id == state.DrawerId)?.Profile.DisplayName ?? "The drawer") +
                    " is choosing from four words. The page will open in a moment.", UiTheme.Muted);
            }
        }
        ImGui.SetCursorScreenPos(saved);
    }

    private static bool WordCard(string id, string word, int index)
    {
        var start = ImGui.GetCursorScreenPos();
        float width = ImGui.GetContentRegionAvail().X;
        var size = new Vector2(width, Math.Max(UiTheme.S(66), ImGui.CalcTextSize(word, false, Math.Max(20, width - UiTheme.S(14))).Y + UiTheme.S(16)));
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
        string kick = state.KickVote is { } k ? $"Kick {k.Votes}/{k.Required}" : "Vote kick";
        string skip = state.SkipVote is { } s ? $"Skip {s.Votes}/{s.Required}" : "Vote skip";
        float width = ImGui.CalcTextSize(kick).X + ImGui.CalcTextSize(skip).X + UiTheme.S(36);
        ImGui.SetCursorScreenPos(painting.RenderOrigin + new Vector2(Math.Max(UiTheme.S(4), painting.RenderSize.X - width - UiTheme.S(4)), painting.RenderSize.Y - UiTheme.S(32)));
        using (ImRaii.Disabled(!state.KickVoteAvailable || state.KickVote?.YouVoted == true))
            if (UiPresentation.SoftButton(kick)) Send(new("vote-kick", TurnId: state.TurnId));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Vote to remove the drawer. Under 5: all other players; 5+: 70%, rounded up. Ballot lasts 30s.");
        ImGui.SameLine();
        using (ImRaii.Disabled(!state.SkipVoteAvailable || state.SkipVote?.YouVoted == true))
            if (UiPresentation.SoftButton(skip)) Send(new("vote-skip", TurnId: state.TurnId));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Vote to skip this turn without removing its drawer. Same threshold as a kick vote.");
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
