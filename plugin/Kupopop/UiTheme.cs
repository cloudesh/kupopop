using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace Kupopop;

/// <summary>Window-scoped stationery palette. No global Dalamud style is changed.</summary>
public static class UiTheme
{
    public static readonly Vector4 Paper = Hex(0xF1E8D3);
    public static readonly Vector4 Panel = Hex(0xFBF6E8);
    public static readonly Vector4 Inset = Hex(0xDED0B4);
    public static readonly Vector4 Cover = Hex(0xB89A91);
    public static readonly Vector4 Tape = Hex(0xE3C990);
    public static readonly Vector4 StickyYellow = Hex(0xF5E7AD);
    public static readonly Vector4 StickyRose = Hex(0xEECFD3);
    public static readonly Vector4 StickySage = Hex(0xDCE7C9);
    public static readonly Vector4 StickyBlue = Hex(0xD7E6EC);
    public static readonly Vector4 Ink = Hex(0x372E39);
    public static readonly Vector4 Muted = Hex(0x70626B);
    public static readonly Vector4 Line = Hex(0xCFBEA1);
    public static readonly Vector4 Brass = Hex(0x876339);
    public static readonly Vector4 Rose = Hex(0x824459);
    public static readonly Vector4 RoseHover = Hex(0x9B536B);
    public static readonly Vector4 RoseTint = Hex(0xEAD6D8);
    public static readonly Vector4 Green = Hex(0x3C6D61);
    public static readonly Vector4 GreenTint = Hex(0xDDE8DE);
    public static readonly Vector4 White = Hex(0xFFF9F1);
    public static float Scale => ImGuiHelpers.GlobalScale;
    public static float S(float value) => value * Scale;
    public static Vector2 V(float x, float y) => new(S(x), S(y));
    public static uint C(Vector4 color) => ImGui.GetColorU32(color);

    private static Vector4 Hex(int rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);

    public static IDisposable Apply() => new ThemeScope();

    private sealed class ThemeScope : IDisposable
    {
        private int colors, styles;
        public ThemeScope()
        {
            Color(ImGuiCol.Text, Ink); Color(ImGuiCol.TextDisabled, Muted);
            Color(ImGuiCol.WindowBg, Cover); Color(ImGuiCol.ChildBg, Panel); Color(ImGuiCol.PopupBg, Panel);
            Color(ImGuiCol.Border, Line); Color(ImGuiCol.BorderShadow, Vector4.Zero);
            Color(ImGuiCol.TitleBg, Inset); Color(ImGuiCol.TitleBgActive, Inset); Color(ImGuiCol.TitleBgCollapsed, Inset);
            Color(ImGuiCol.FrameBg, Hex(0xE9DFC7)); Color(ImGuiCol.FrameBgHovered, Hex(0xE0D2B3)); Color(ImGuiCol.FrameBgActive, White);
            Color(ImGuiCol.Button, Inset); Color(ImGuiCol.ButtonHovered, Hex(0xDED0BB)); Color(ImGuiCol.ButtonActive, Hex(0xD1BFA9));
            Color(ImGuiCol.Header, RoseTint); Color(ImGuiCol.HeaderHovered, Hex(0xE2CBCF)); Color(ImGuiCol.HeaderActive, RoseTint);
            Color(ImGuiCol.CheckMark, Rose); Color(ImGuiCol.SliderGrab, Rose); Color(ImGuiCol.SliderGrabActive, RoseHover);
            Color(ImGuiCol.Separator, Line); Color(ImGuiCol.SeparatorHovered, Brass); Color(ImGuiCol.SeparatorActive, Rose);
            Color(ImGuiCol.ScrollbarBg, Paper); Color(ImGuiCol.ScrollbarGrab, Hex(0xC3B399));
            Color(ImGuiCol.ScrollbarGrabHovered, Brass); Color(ImGuiCol.ScrollbarGrabActive, Rose);
            Color(ImGuiCol.TextSelectedBg, RoseTint); Color(ImGuiCol.TableHeaderBg, Inset);
            Color(ImGuiCol.TableBorderLight, Line); Color(ImGuiCol.TableBorderStrong, Line);
            Color(ImGuiCol.ResizeGrip, Vector4.Zero); Color(ImGuiCol.ResizeGripHovered, Brass); Color(ImGuiCol.ResizeGripActive, Rose);
            Style(ImGuiStyleVar.WindowPadding, V(34, 18)); Style(ImGuiStyleVar.FramePadding, V(8, 5));
            Style(ImGuiStyleVar.ItemSpacing, V(6, 6)); Style(ImGuiStyleVar.ItemInnerSpacing, V(5, 4));
            Style(ImGuiStyleVar.CellPadding, V(4, 3));
            Style(ImGuiStyleVar.WindowRounding, S(6)); Style(ImGuiStyleVar.ChildRounding, S(2));
            Style(ImGuiStyleVar.FrameRounding, S(3)); Style(ImGuiStyleVar.PopupRounding, S(4));
            Style(ImGuiStyleVar.WindowBorderSize, S(1)); Style(ImGuiStyleVar.ChildBorderSize, S(1));
            Style(ImGuiStyleVar.FrameBorderSize, 0); Style(ImGuiStyleVar.ScrollbarSize, S(9));
            Style(ImGuiStyleVar.GrabRounding, S(4));
        }
        private void Color(ImGuiCol index, Vector4 value) { ImGui.PushStyleColor(index, value); colors++; }
        private void Style(ImGuiStyleVar index, Vector2 value) { ImGui.PushStyleVar(index, value); styles++; }
        private void Style(ImGuiStyleVar index, float value) { ImGui.PushStyleVar(index, value); styles++; }
        public void Dispose() { ImGui.PopStyleVar(styles); ImGui.PopStyleColor(colors); }
    }

    public static void Caption(string text) => ImGui.TextColored(Muted, text);
    public static void Wrapped(string text, Vector4? color = null)
    {
        using var tint = ImRaii.PushColor(ImGuiCol.Text, color ?? Ink);
        ImGui.PushTextWrapPos(0);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }
    public static void Gap(float size = 4) => ImGui.Dummy(V(0, size));
    public static void Rule() => ImGui.Separator();

    // Deterministic native geometry, not a generated or downloaded bitmap. Keep it
    // sparse and stationary, and keep the synchronized drawing pixels untouched.
    public static void PaperGrain(Vector2 min, Vector2 max, Vector4? tint = null)
    {
        if (UiAssets.PaperTexture(min, max, tint)) return;
        var draw = ImGui.GetWindowDrawList();
        var fiber = Brass; fiber.W = 0.10f;
        uint color = C(fiber);
        float step = S(16);
        int rows = Math.Min(70, (int)((max.Y - min.Y) / step));
        int columns = Math.Min(90, (int)((max.X - min.X) / step));
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < columns; col++)
            {
                uint hash = unchecked((uint)(row * 73856093 ^ col * 19349663)) * 2654435761u;
                var point = min + new Vector2(col * step + S(2 + hash % 10), row * step + S(2 + (hash >> 8) % 10));
                draw.AddLine(point, point + V(1 + (hash >> 16) % 3, 0.3f), color, S(0.6f));
            }
    }

    public static void SheetEdges(Vector2 min, Vector2 max)
    {
        float extent = Math.Max(max.X - min.X, max.Y - min.Y);
        float degrees = Math.Min(1.7f, MathF.Atan2(S(4), extent / 2) * (180 / MathF.PI));
        UiStationery.PaperLeaf(min, max, -degrees, V(-1, 2), Inset);
        UiStationery.PaperLeaf(min, max, degrees * 0.7f, V(2, -1), Panel);
    }

    public static bool Button(string label, float width = 0, bool primary = false, bool quiet = false)
    {
        using var normal = ImRaii.PushColor(ImGuiCol.Button, primary ? Rose : quiet ? Vector4.Zero : Inset);
        using var hover = ImRaii.PushColor(ImGuiCol.ButtonHovered, primary ? RoseHover : RoseTint);
        using var active = ImRaii.PushColor(ImGuiCol.ButtonActive, primary ? Rose : Inset);
        using var text = ImRaii.PushColor(ImGuiCol.Text, primary ? White : Ink);
        bool clicked = ImGui.Button(label, new Vector2(width, S(28)));
        if (!quiet)
        {
            var a = ImGui.GetItemRectMin(); var b = ImGui.GetItemRectMax();
            var stroke = primary ? White : Brass; stroke.W = 0.25f;
            ImGui.GetWindowDrawList().AddLine(a + V(5, 3), new Vector2(b.X - S(5), a.Y + S(3)), C(stroke));
        }
        return clicked;
    }

    public static bool Tab(string label, bool selected)
    {
        using var fill = ImRaii.PushColor(ImGuiCol.Button, selected ? RoseTint : Panel);
        using var text = ImRaii.PushColor(ImGuiCol.Text, selected ? Rose : Muted);
        bool clicked = ImGui.Button(label, new Vector2(ImGui.CalcTextSize(label).X + S(22), S(28)));
        var a = ImGui.GetItemRectMin(); var b = ImGui.GetItemRectMax();
        ImGui.GetWindowDrawList().AddLine(new Vector2(a.X + S(6), b.Y - S(3)), new Vector2(b.X - S(6), b.Y - S(3)), C(selected ? Rose : Line), S(selected ? 2 : 1));
        return clicked;
    }

    public static bool NoteButton(string label, float width, Vector4 paper, bool primary = false)
    {
        using var normal = ImRaii.PushColor(ImGuiCol.Button, Vector4.Lerp(paper, Ink, 0.055f));
        using var hover = ImRaii.PushColor(ImGuiCol.ButtonHovered, Vector4.Lerp(paper, Ink, 0.10f));
        using var active = ImRaii.PushColor(ImGuiCol.ButtonActive, Vector4.Lerp(paper, Ink, 0.16f));
        using var text = ImRaii.PushColor(ImGuiCol.Text, primary ? Rose : Ink);
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, S(1));
        bool clicked = ImGui.Button(label, new Vector2(width, S(28)));
        var a = ImGui.GetItemRectMin(); var b = ImGui.GetItemRectMax();
        var rule = primary ? Rose : Brass; rule.W = primary ? 0.65f : 0.4f;
        var draw = ImGui.GetWindowDrawList();
        draw.AddLine(new Vector2(a.X + S(6), b.Y - S(4)), new Vector2(b.X - S(6), b.Y - S(4)), C(rule), S(1));
        return clicked;
    }

    public static void Field(string label, string id, ref string value, int maximum, string hint = "", ImGuiInputTextFlags flags = ImGuiInputTextFlags.None)
    {
        Caption(label);
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint(id, hint, ref value, maximum, flags);
    }

    public static void Ellipsis(string text, float width)
    {
        if (ImGui.CalcTextSize(text).X <= width) { ImGui.TextUnformatted(text); return; }
        var elements = StringInfo.ParseCombiningCharacters(text);
        int low = 0, high = elements.Length;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            string candidate = text[..(mid == elements.Length ? text.Length : elements[mid])] + "...";
            if (ImGui.CalcTextSize(candidate).X <= width) low = mid; else high = mid - 1;
        }
        ImGui.TextUnformatted(text[..(low == elements.Length ? text.Length : elements[low])] + "...");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(text.Replace("%", "%%"));
    }

    public ref struct PaperPanel
    {
        private ImRaii.ChildDisposable child;
        public bool Success { get; }
        public PaperPanel(string id, Vector2 size, bool inset = false, bool compact = false, bool taped = false, Vector4? note = null, bool scrollable = true)
        {
            using var color = ImRaii.PushColor(ImGuiCol.ChildBg, Vector4.Zero);
            using var border = ImRaii.PushColor(ImGuiCol.Border, Vector4.Zero);
            using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, compact ? V(10, note.HasValue ? 22 : 10) : V(14, taped || note.HasValue ? 24 : 14));
            var flags = scrollable ? ImGuiWindowFlags.None : ImGuiWindowFlags.NoScrollbar;
            var scope = ImRaii.Child(id, size, true, flags);
            child = scope;
            Success = scope.Success;
            if (Success)
            {
                var min = ImGui.GetWindowPos() + V(6, 6);
                var max = ImGui.GetWindowPos() + ImGui.GetWindowSize() - V(6, 6);
                var draw = ImGui.GetWindowDrawList();
                draw.PushClipRect(ImGui.GetWindowPos() + V(1, 1), ImGui.GetWindowPos() + ImGui.GetWindowSize() - V(1, 1), false);
                var surface = note ?? (inset ? Inset : Panel);
                if (note.HasValue)
                {
                    var shadow = Ink; shadow.W = 0.12f;
                    draw.AddRectFilled(min + V(1, 3), max + V(1, 3), C(shadow));
                }
                else SheetEdges(min, max);
                draw.AddRectFilled(min, max, C(surface), S(1));
                var outline = note.HasValue ? Vector4.Lerp(surface, Brass, 0.3f) : Line;
                draw.AddRect(min, max, C(outline), S(1));
                PaperGrain(min, max, note);
                if (note.HasValue)
                {
                    var glue = Ink; glue.W = 0.035f;
                    draw.AddRectFilled(min, new Vector2(max.X, min.Y + S(13)), C(glue));
                    var edge = Brass; edge.W = 0.18f;
                    draw.AddLine(min + V(1, 13), new Vector2(max.X - S(1), min.Y + S(13)), C(edge));
                }
                if (taped) UiStationery.Tape(new Vector2(min.X + (max.X - min.X) * 0.5f - S(25), min.Y + S(2)), S(50));
                var curl = note.HasValue ? Vector4.Lerp(surface, Inset, 0.4f) : Inset;
                draw.AddTriangleFilled(max - V(12, 0), max - V(0, 12), max, C(curl));
                draw.AddLine(max - V(12, 0), max - V(0, 12), C(Line));
                draw.PopClipRect();
            }
        }
        public void Dispose() => child.Dispose();
    }
}
