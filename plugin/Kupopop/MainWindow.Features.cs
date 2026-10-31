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
        var origin = painting.RenderOrigin + UiTheme.V(12, 18);
        ImGui.SetCursorScreenPos(origin);
        using (ImRaii.PushColor(ImGuiCol.ChildBg, UiTheme.Panel))
        using (var panel = ImRaii.Child("word-choices", new Vector2(Math.Max(60, painting.RenderSize.X - UiTheme.S(24)), Math.Max(60, painting.RenderSize.Y - UiTheme.S(36))), false))
        {
            if (panel.Success)
            {
                art.Heading(state.DrawerId == device?.AccountId ? "Choose your word" : "A little inspiration...");
                if (state.WordChoices is { Length: > 0 } choices)
                {
                    UiTheme.Wrapped("Choose one. Your drawing timer starts afterwards.", UiTheme.Muted);
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
        draw.AddRectFilled(start, start + size, UiTheme.C(color), UiTheme.S(2));
        draw.AddRect(start, start + size, UiTheme.C(hovered ? UiTheme.Rose : UiTheme.Line), UiTheme.S(2));
        ImGui.SetCursorScreenPos(start + UiTheme.V(7, 8));
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
            if (UiTheme.Button(kick, 0, quiet: false)) Send(new("vote-kick", TurnId: state.TurnId));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Vote to remove the drawer. Under 5: all other players; 5+: 70%, rounded up. Ballot lasts 30s.");
        ImGui.SameLine();
        using (ImRaii.Disabled(!state.SkipVoteAvailable || state.SkipVote?.YouVoted == true))
            if (UiTheme.Button(skip, 0, quiet: false)) Send(new("vote-skip", TurnId: state.TurnId));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Vote to skip this turn without removing its drawer. Same threshold as a kick vote.");
        ImGui.SetCursorScreenPos(saved);
    }

    private static void DrawChatCard(ChatLine line)
    {
        float width = ImGui.GetContentRegionAvail().X, pad = UiTheme.S(5);
        var start = ImGui.GetCursorScreenPos();
        float indent = line.System ? UiTheme.S(19) : 0;
        float textHeight = ImGui.CalcTextSize(line.Text, false, Math.Max(20, width - pad * 2 - indent)).Y;
        float height = textHeight + pad * 2 + (line.System ? 0 : ImGui.GetTextLineHeight() + UiTheme.S(2));
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(start, start + new Vector2(width, height), UiTheme.C(line.System ? UiTheme.StickySage : UiTheme.Panel), UiTheme.S(2));
        draw.AddRect(start, start + new Vector2(width, height), UiTheme.C(UiTheme.Line), UiTheme.S(2));
        ImGui.SetCursorScreenPos(start + new Vector2(pad, pad));
        ImGui.BeginGroup();
        if (line.System)
        {
            UiAssets.Glyph(FontAwesomeIcon.InfoCircle, UiTheme.Green);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Game / server message");
            ImGui.SameLine(0, UiTheme.S(4));
        }
        else
        {
            using var color = ImRaii.PushColor(ImGuiCol.Text, line.Done ? UiTheme.Rose : UiTheme.Brass);
            UiTheme.Ellipsis(line.Name, width - pad * 2);
        }
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - pad * 2 - indent);
        ImGui.TextUnformatted(line.Text); ImGui.PopTextWrapPos();
        ImGui.EndGroup();
        ImGui.SetCursorScreenPos(start + new Vector2(0, height));
        ImGui.Dummy(new Vector2(width, 0));
    }

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
                    (state.FirstGuessers?.Length ?? 1) * ImGui.GetTextLineHeightWithSpacing();
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
                    for (int i = 0; i < winners.Length; i++) ImGui.TextUnformatted($"{i + 1}. {winners[i].Name}   +{winners[i].Points}");
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
