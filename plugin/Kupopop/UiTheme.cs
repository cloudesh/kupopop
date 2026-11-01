using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace Kupopop;

/// <summary>Window-scoped stationery palette. No global Dalamud style is changed.</summary>
public static class UiTheme
{
    public static bool DarkMode { get; private set; }
    public static Vector4 Paper => Hex(DarkMode ? 0x22252D : 0xF1E8D3);
    public static Vector4 Panel => Hex(DarkMode ? 0x2B303B : 0xFBF6E8);
    public static Vector4 Inset => Hex(DarkMode ? 0x39414F : 0xDED0B4);
    public static Vector4 Cover => Hex(DarkMode ? 0x414858 : 0xB89A91);
    public static Vector4 Tape => Hex(DarkMode ? 0x8E744B : 0xE3C990);
    public static Vector4 StickyYellow => Hex(DarkMode ? 0x414032 : 0xF5E7AD);
    public static Vector4 StickyRose => Hex(DarkMode ? 0x453441 : 0xEECFD3);
    public static Vector4 StickySage => Hex(DarkMode ? 0x30423B : 0xDCE7C9);
    public static Vector4 StickyBlue => Hex(DarkMode ? 0x2F4050 : 0xD7E6EC);
    public static Vector4 InvitationPaper => Hex(DarkMode ? 0x513949 : 0xF3CCD9);
    public static Vector4 Ink => Hex(DarkMode ? 0xF2EEE7 : 0x372E39);
    public static Vector4 Muted => Hex(DarkMode ? 0xB9C1CF : 0x70626B);
    public static Vector4 Line => Hex(DarkMode ? 0x556171 : 0xCFBEA1);
    public static Vector4 Brass => Hex(DarkMode ? 0xDFC794 : 0x876339);
    public static Vector4 Rose => Hex(DarkMode ? 0xEDADC4 : 0x824459);
    public static Vector4 RoseHover => Hex(DarkMode ? 0xF5C5D7 : 0x9B536B);
    public static Vector4 RoseTint => Hex(DarkMode ? 0x503A4B : 0xEAD6D8);
    public static Vector4 Green => Hex(DarkMode ? 0x9BD9C3 : 0x3C6D61);
    public static Vector4 GreenTint => Hex(DarkMode ? 0x2E483C : 0xDDE8DE);
    public static readonly Vector4 White = Hex(0xFFF9F1);
    public static readonly Vector4 CanvasInk = Hex(0x372E39);
    public static readonly Vector4 CanvasAccent = Hex(0x824459);
    public static Vector4 Shadow => DarkMode ? Hex(0x050407) : Ink;
    public static Vector4 OnAccent => DarkMode ? Hex(0x251D23) : White;
    public static float Scale => ImGuiHelpers.GlobalScale;
    public static float S(float value) => value * Scale;
    public static Vector2 V(float x, float y) => new(S(x), S(y));
    public static uint C(Vector4 color) => ImGui.GetColorU32(color);

    private static Vector4 Hex(int rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);

    public static IDisposable Apply(bool compactRoom = false, bool darkMode = false) => new ThemeScope(compactRoom, darkMode);

    private sealed class ThemeScope : IDisposable
    {
        private int colors, styles;
        private readonly bool previousMode;
        public ThemeScope(bool compactRoom, bool darkMode)
        {
            previousMode = DarkMode; DarkMode = darkMode;
            Color(ImGuiCol.Text, Ink); Color(ImGuiCol.TextDisabled, Muted);
            Color(ImGuiCol.WindowBg, Cover); Color(ImGuiCol.ChildBg, Panel); Color(ImGuiCol.PopupBg, Panel);
            Color(ImGuiCol.Border, Line); Color(ImGuiCol.BorderShadow, Vector4.Zero);
            Color(ImGuiCol.TitleBg, Inset); Color(ImGuiCol.TitleBgActive, Inset); Color(ImGuiCol.TitleBgCollapsed, Inset);
            Color(ImGuiCol.FrameBg, Hex(DarkMode ? 0x333B48 : 0xE9DFC7)); Color(ImGuiCol.FrameBgHovered, Hex(DarkMode ? 0x414C5C : 0xE0D2B3)); Color(ImGuiCol.FrameBgActive, DarkMode ? Inset : White);
            Color(ImGuiCol.Button, Inset); Color(ImGuiCol.ButtonHovered, Vector4.Lerp(Inset, Ink, 0.08f)); Color(ImGuiCol.ButtonActive, Vector4.Lerp(Inset, Ink, 0.16f));
            Color(ImGuiCol.Header, RoseTint); Color(ImGuiCol.HeaderHovered, Vector4.Lerp(RoseTint, Rose, 0.15f)); Color(ImGuiCol.HeaderActive, RoseTint);
            Color(ImGuiCol.CheckMark, Rose); Color(ImGuiCol.SliderGrab, Rose); Color(ImGuiCol.SliderGrabActive, RoseHover);
            Color(ImGuiCol.Separator, Line); Color(ImGuiCol.SeparatorHovered, Brass); Color(ImGuiCol.SeparatorActive, Rose);
            Color(ImGuiCol.ScrollbarBg, Paper); Color(ImGuiCol.ScrollbarGrab, Hex(DarkMode ? 0x65748B : 0xC3B399));
            Color(ImGuiCol.ScrollbarGrabHovered, Brass); Color(ImGuiCol.ScrollbarGrabActive, Rose);
            Color(ImGuiCol.TextSelectedBg, RoseTint); Color(ImGuiCol.TableHeaderBg, Inset);
            Color(ImGuiCol.TableBorderLight, Line); Color(ImGuiCol.TableBorderStrong, Line);
            Color(ImGuiCol.ResizeGrip, Vector4.Zero); Color(ImGuiCol.ResizeGripHovered, Brass); Color(ImGuiCol.ResizeGripActive, Rose);
            Style(ImGuiStyleVar.WindowPadding, V(34, 8)); Style(ImGuiStyleVar.FramePadding, V(8, 5));
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
        public void Dispose() { ImGui.PopStyleVar(styles); ImGui.PopStyleColor(colors); DarkMode = previousMode; }
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
    public static void InvitationGlow(Vector2 min, Vector2 max)
    {
        var draw = ImGui.GetWindowDrawList();
        var top = UiPresentation.Alpha(Rose, DarkMode ? 0.16f : 0.14f);
        var bottom = UiPresentation.Alpha(Rose, 0);
        draw.AddRectFilledMultiColor(min + V(7, 7), max - V(7, 7), C(top), C(top), C(bottom), C(bottom));
        draw.AddRectFilled(min + V(7, 7), new Vector2(min.X + S(10), max.Y - S(7)), C(Rose));
    }
    public static void Rule() => ImGui.Separator();

    public static void Tooltip(string text)
    {
        if (ImGui.IsMouseDown(ImGuiMouseButton.Left) || ImGui.IsMouseDown(ImGuiMouseButton.Right)) return;
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, V(8, 5));
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, S(4));
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + S(300));
        ImGui.TextUnformatted(text); ImGui.PopTextWrapPos(); ImGui.EndTooltip();
    }

    public static unsafe bool GlyphButton(string id, string glyph, Vector2 size)
    {
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, Vector2.Zero);
        using var align = ImRaii.PushStyle(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f));
        bool clicked = ImGui.Button("##" + id, size);
        var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
        var font = ImGui.GetFont();
        var inkMin = Vector2.Zero; var inkSize = ImGui.CalcTextSize(glyph);
        if (glyph.Length == 1)
        {
            var metric = font.FindGlyph(glyph[0]);
            if (metric != null)
            {
                float scale = ImGui.GetFontSize() / font.FontSize;
                inkMin = new Vector2(metric->X0, metric->Y0) * scale;
                inkSize = new Vector2(metric->X1 - metric->X0, metric->Y1 - metric->Y0) * scale;
            }
        }
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(min, max, true);
        draw.AddText(min + (max - min - inkSize) / 2 - inkMin, ImGui.GetColorU32(ImGuiCol.Text), glyph);
        draw.PopClipRect();
        return clicked;
    }

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
        using var text = ImRaii.PushColor(ImGuiCol.Text, primary ? OnAccent : Ink);
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
        using var fill = ImRaii.PushColor(ImGuiCol.Button, selected ? Panel : Vector4.Zero);
        using var hover = ImRaii.PushColor(ImGuiCol.ButtonHovered, RoseTint);
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, S(5));
        using var text = ImRaii.PushColor(ImGuiCol.Text, selected ? Rose : Muted);
        bool clicked = ImGui.Button(label, new Vector2(ImGui.CalcTextSize(label).X + S(26), S(30)));
        if (selected) ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), C(UiPresentation.Alpha(Line, 0.55f)), S(5));
        return clicked;
    }

    public static bool PanelTab(string label, bool selected)
    {
        var start = ImGui.GetCursorScreenPos();
        var size = new Vector2(ImGui.CalcTextSize(label).X + S(30), S(34));
        bool clicked = ImGui.InvisibleButton(label, size);
        var fill = selected ? Paper : ImGui.IsItemHovered() ? RoseTint : Inset;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(start, start + size, C(fill), S(5), ImDrawFlags.RoundCornersTop);
        if (selected)
        {
            draw.AddRectFilled(start + new Vector2(0, size.Y - 1), start + size + new Vector2(0, 2), C(Panel));
            draw.AddLine(start + V(9, 2), start + new Vector2(size.X - S(9), S(2)), C(Rose), S(2));
        }
        draw.AddText(start + (size - ImGui.CalcTextSize(label)) / 2, C(selected ? Ink : Muted), label);
        return clicked;
    }

    public static bool ListButton(string id, string name, string detail, bool selected)
    {
        using var unique = ImRaii.PushId(id);
        using var normal = ImRaii.PushColor(ImGuiCol.Button, selected ? RoseTint : UiPresentation.Alpha(Inset, 0.55f));
        using var hover = ImRaii.PushColor(ImGuiCol.ButtonHovered, Vector4.Lerp(RoseTint, Rose, 0.12f));
        using var text = ImRaii.PushColor(ImGuiCol.Text, selected ? Ink : Muted);
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, V(12, 7));
        using var align = ImRaii.PushStyle(ImGuiStyleVar.ButtonTextAlign, new Vector2(0, 0.5f));
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, S(5));
        bool clicked = ImGui.Button(name + "\n" + detail + "##list", new Vector2(ImGui.GetContentRegionAvail().X, S(52)));
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
        if (ImGui.IsItemHovered()) UiTheme.Tooltip(text);
    }

    public ref struct PaperPanel
    {
        private ImRaii.ChildDisposable child;
        public bool Success { get; }
        public PaperPanel(string id, Vector2 size, bool inset = false, bool compact = false, bool taped = false, Vector4? note = null, bool scrollable = true, bool plainTop = false, bool flush = false, bool attachedTabs = false)
        {
            using var color = ImRaii.PushColor(ImGuiCol.ChildBg, Vector4.Zero);
            using var border = ImRaii.PushColor(ImGuiCol.Border, Vector4.Zero);
            using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, compact ? V(10, note.HasValue && !plainTop ? 22 : 10) : V(14, taped || note.HasValue ? 24 : 14));
            var flags = scrollable ? ImGuiWindowFlags.None : ImGuiWindowFlags.NoScrollbar;
            var scope = ImRaii.Child(id, size, true, flags);
            child = scope;
            Success = scope.Success;
            if (Success)
            {
                var min = ImGui.GetWindowPos() + (flush ? Vector2.Zero : V(6, 6));
                var max = ImGui.GetWindowPos() + ImGui.GetWindowSize() - (flush ? Vector2.Zero : V(6, 6));
                var draw = ImGui.GetWindowDrawList();
                draw.PushClipRect(ImGui.GetWindowPos() + V(1, 1), ImGui.GetWindowPos() + ImGui.GetWindowSize() - V(1, 1), false);
                var surface = note ?? (inset ? Inset : Panel);
                if (note.HasValue)
                {
                    var shadow = Shadow; shadow.W = 0.12f;
                    draw.AddRectFilled(min + V(1, 3), max + V(1, 3), C(shadow));
                }
                else if (!attachedTabs) SheetEdges(min, max);
                if (!attachedTabs) draw.AddRectFilled(min, max, C(surface), S(1));
                var outline = note.HasValue ? Vector4.Lerp(surface, Brass, 0.3f) : Line;
                if (attachedTabs)
                {
                    draw.AddLine(min, new Vector2(min.X, max.Y), C(outline));
                    draw.AddLine(new Vector2(min.X, max.Y), max, C(outline));
                    draw.AddLine(max, new Vector2(max.X, min.Y), C(outline));
                }
                else draw.AddRect(min, max, C(outline), S(1));
                if (!attachedTabs) PaperGrain(min, max, note);
                if (note.HasValue && !plainTop)
                {
                    var glue = Ink; glue.W = 0.035f;
                    draw.AddRectFilled(min, new Vector2(max.X, min.Y + S(13)), C(glue));
                    var edge = Brass; edge.W = 0.18f;
                    draw.AddLine(min + V(1, 13), new Vector2(max.X - S(1), min.Y + S(13)), C(edge));
                }
                if (taped) UiStationery.Tape(new Vector2(min.X + (max.X - min.X) * 0.5f - S(25), min.Y + S(2)), S(50));
                if (!attachedTabs)
                {
                    var curl = note.HasValue ? Vector4.Lerp(surface, Inset, 0.4f) : Inset;
                    draw.AddTriangleFilled(max - V(12, 0), max - V(0, 12), max, C(curl));
                    draw.AddLine(max - V(12, 0), max - V(0, 12), C(Line));
                }
                draw.PopClipRect();
            }
        }
        public void Dispose() => child.Dispose();
    }
}
