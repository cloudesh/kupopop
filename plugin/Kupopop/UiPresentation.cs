using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Kupopop.Shared;

namespace Kupopop;

public static class UiPresentation
{
    public static void DrawCenteredText(string text, Vector2 origin, Vector2 size)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            string line = "";
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string next = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && ImGui.CalcTextSize(next).X > size.X) { lines.Add(line); line = word; }
                else line = next;
            }
            lines.Add(line);
        }
        float height = ImGui.GetTextLineHeight(), y = origin.Y + Math.Max(0, (size.Y - height * lines.Count) / 2);
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(origin, origin + size, true);
        foreach (var line in lines)
        {
            draw.AddText(new Vector2(origin.X + Math.Max(0, (size.X - ImGui.CalcTextSize(line).X) / 2), y), UiTheme.C(UiTheme.Ink), line);
            y += height;
        }
        draw.PopClipRect();
    }

    public static Vector4 Alpha(Vector4 color, float alpha) => new(color.X, color.Y, color.Z, alpha);

    private static (Vector2 Size, List<(char Letter, Vector2 Offset)> Slots) HintLayout(string hint, float width)
    {
        width = Math.Max(UiTheme.S(20), width);
        float cell = Math.Max(UiTheme.S(14), ImGui.GetFontSize() * 0.68f), gap = UiTheme.S(4);
        float rowHeight = ImGui.GetTextLineHeight() + UiTheme.S(5), x = 0, y = 0, used = 0;
        var slots = new List<(char, Vector2)>(hint.Length);
        foreach (char letter in hint)
        {
            float advance = char.IsWhiteSpace(letter) ? cell * 0.65f : cell + gap;
            if (x > 0 && x + cell > width) { x = 0; y += rowHeight; }
            if (!char.IsWhiteSpace(letter)) slots.Add((letter, new Vector2(x, y)));
            x += advance; used = Math.Max(used, x);
        }
        return (new Vector2(Math.Min(width, used), y + rowHeight), slots);
    }

    public static Vector2 MeasureHint(string hint, float width) => HintLayout(hint, width).Size;

    public static void Hint(string hint, float width = 0)
    {
        var origin = ImGui.GetCursorScreenPos();
        var layout = HintLayout(hint, width > 0 ? width : ImGui.GetContentRegionAvail().X);
        float cell = Math.Max(UiTheme.S(14), ImGui.GetFontSize() * 0.68f), rowHeight = ImGui.GetTextLineHeight() + UiTheme.S(5);
        var draw = ImGui.GetWindowDrawList();
        foreach (var (letter, offset) in layout.Slots)
        {
                var position = origin + offset;
                if (letter == '_')
                    draw.AddLine(position + new Vector2(2, rowHeight - UiTheme.S(5)),
                        position + new Vector2(cell - 2, rowHeight - UiTheme.S(5)), UiTheme.C(UiTheme.Muted), UiTheme.S(1.5f));
                else
                {
                    string text = char.ToUpperInvariant(letter).ToString();
                    draw.AddText(position + new Vector2((cell - ImGui.CalcTextSize(text).X) / 2, 0), UiTheme.C(UiTheme.Ink), text);
                }
        }
        ImGui.Dummy(layout.Size);
        if (ImGui.IsItemHovered()) UiTheme.Tooltip($"{hint.Count(c => c == '_' || char.IsLetterOrDigit(c))} letters");
    }

    public static bool SoftButton(string label)
    {
        using var normal = ImRaii.PushColor(ImGuiCol.Button, Alpha(UiTheme.Panel, UiTheme.DarkMode ? 0.82f : 0.64f));
        using var hover = ImRaii.PushColor(ImGuiCol.ButtonHovered, Alpha(UiTheme.Panel, UiTheme.DarkMode ? 0.92f : 0.82f));
        using var active = ImRaii.PushColor(ImGuiCol.ButtonActive, Alpha(UiTheme.Panel, UiTheme.DarkMode ? 0.97f : 0.94f));
        using var border = ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, 0);
        return ImGui.Button(label, new Vector2(0, UiTheme.S(26)));
    }

    public static void ChatCard(ChatLine line)
    {
        float width = Math.Max(20, ImGui.GetContentRegionAvail().X - UiTheme.S(2)), pad = UiTheme.S(5);
        var start = ImGui.GetCursorScreenPos() + UiTheme.V(1, 0);
        float icon = line.System || line.Done ? ImGui.GetFontSize() + UiTheme.S(4) : 0;
        float bodyWidth = Math.Max(20, width - pad * 2 - (line.System ? icon : 0));
        float height = ImGui.CalcTextSize(line.Text, false, bodyWidth).Y + pad * 2 +
            (line.System ? 0 : ImGui.GetTextLineHeight() + UiTheme.S(2));
        var draw = ImGui.GetWindowDrawList();
        var fill = line.Done ? Alpha(UiTheme.Muted, 0.14f) : Alpha(UiTheme.DarkMode ? UiTheme.Panel : UiTheme.White, line.System ? 0.48f : 0.40f);
        draw.AddRectFilled(start, start + new Vector2(width, height), UiTheme.C(fill), UiTheme.S(3));
        ImGui.SetCursorScreenPos(start + new Vector2(pad, pad));
        ImGui.BeginGroup();
        using (ImRaii.PushColor(ImGuiCol.Text, line.Done ? UiTheme.Muted : UiTheme.Ink))
        {
            if (line.System)
            {
                UiAssets.Glyph(FontAwesomeIcon.InfoCircle, UiTheme.Green);
                if (ImGui.IsItemHovered()) UiTheme.Tooltip("Game / server message");
                ImGui.SameLine(0, UiTheme.S(4));
            }
            else
            {
                if (line.Done)
                {
                    UiAssets.Glyph(FontAwesomeIcon.CheckCircle, UiTheme.Muted);
                    if (ImGui.IsItemHovered()) UiTheme.Tooltip("Done mode: drawer and correct guessers only");
                    ImGui.SameLine(0, UiTheme.S(4));
                }
                using var name = ImRaii.PushColor(ImGuiCol.Text, line.Done ? UiTheme.Muted : UiTheme.Brass);
                UiTheme.Ellipsis(line.Name, Math.Max(20, width - pad * 2 - icon));
            }
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + bodyWidth);
            ImGui.TextUnformatted(line.Text); ImGui.PopTextWrapPos();
        }
        ImGui.EndGroup();
        ImGui.SetCursorScreenPos(start + new Vector2(-UiTheme.S(1), height));
        ImGui.Dummy(new Vector2(width, 0));
    }

    public static void Podium(SolveResult[] winners, float width, bool totals = false, Action<SolveResult, Vector2, float>? portrait = null)
    {
        var origin = ImGui.GetCursorScreenPos();
        float column = width / 3, baseline = UiTheme.S(154);
        var draw = ImGui.GetWindowDrawList();
        for (int slot = 0; slot < 3; slot++)
        {
            int rank = slot == 0 ? 1 : slot == 1 ? 0 : 2;
            if (rank >= winners.Length) continue;
            var winner = winners[rank];
            float height = UiTheme.S(rank == 0 ? 62 : rank == 1 ? 44 : 32);
            var position = origin + new Vector2(slot * column + UiTheme.S(4), baseline - height);
            var size = new Vector2(column - UiTheme.S(8), height);
            var tint = rank == 0 ? UiTheme.StickyYellow : rank == 1 ? UiTheme.StickyBlue : UiTheme.StickyRose;
            draw.AddRectFilled(position, position + size, UiTheme.C(Alpha(tint, 0.84f)), UiTheme.S(3));
            int place = totals ? Array.FindIndex(winners, w => w.Points == winner.Points) + 1 : rank + 1;
            string number = place.ToString();
            draw.AddText(position + (size - ImGui.CalcTextSize(number)) / 2, UiTheme.C(UiTheme.Ink), number);
            var portraitAt = position + new Vector2((size.X - UiTheme.S(32)) / 2, -UiTheme.S(75));
            draw.AddCircleFilled(portraitAt + UiTheme.V(16, 16), UiTheme.S(19), UiTheme.C(Alpha(tint, 0.8f)));
            portrait?.Invoke(winner, portraitAt, UiTheme.S(32));
            if (rank == 0)
            {
                var crown = portraitAt + UiTheme.V(8, -9);
                draw.AddQuadFilled(crown, crown + UiTheme.V(16, 0), crown + UiTheme.V(14, 7), crown + UiTheme.V(2, 7), UiTheme.C(UiTheme.Brass));
                for (int point = 0; point < 3; point++) draw.AddTriangleFilled(crown + UiTheme.V(point * 6, 1), crown + UiTheme.V(point * 6 + 2, -5), crown + UiTheme.V(point * 6 + 4, 1), UiTheme.C(UiTheme.Brass));
            }
            ImGui.SetCursorScreenPos(position + new Vector2(0, -ImGui.GetTextLineHeightWithSpacing() * 2 - UiTheme.S(4)));
            CenteredLabel(winner.Name, size.X, UiTheme.Ink);
            ImGui.SetCursorScreenPos(new Vector2(position.X, position.Y - ImGui.GetTextLineHeightWithSpacing() - UiTheme.S(2)));
            CenteredLabel((totals ? "" : "+") + winner.Points.ToString("N0") + (totals ? " pts" : ""), size.X, UiTheme.Muted);
        }
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, baseline + UiTheme.S(4)));
    }

    public static void CenteredLabel(string label, float width, Vector4 color)
    {
        string text = label;
        while (text.Length > 1 && ImGui.CalcTextSize(text).X > width - UiTheme.S(4)) text = text[..^1];
        if (text.Length < label.Length && text.Length > 2) text = text[..^2] + "..";
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (width - ImGui.CalcTextSize(text).X) / 2));
        ImGui.TextColored(color, text);
        if (ImGui.IsItemHovered() && text != label) UiTheme.Tooltip(label);
    }
}
