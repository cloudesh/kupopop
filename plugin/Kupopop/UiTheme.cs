using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace Kupopop;

/// <summary>Window-scoped stationery palette. No global Dalamud style is changed.</summary>
public static class UiTheme
{
    public static readonly Vector4 Paper = Hex(0xEEE7D9);
    public static readonly Vector4 Panel = Hex(0xF8F3E9);
    public static readonly Vector4 Inset = Hex(0xE5DCCB);
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
            Color(ImGuiCol.WindowBg, Paper); Color(ImGuiCol.ChildBg, Panel); Color(ImGuiCol.PopupBg, Panel);
            Color(ImGuiCol.Border, Line); Color(ImGuiCol.BorderShadow, Vector4.Zero);
            Color(ImGuiCol.TitleBg, Inset); Color(ImGuiCol.TitleBgActive, Inset); Color(ImGuiCol.TitleBgCollapsed, Inset);
            Color(ImGuiCol.FrameBg, Inset); Color(ImGuiCol.FrameBgHovered, Hex(0xDED0BB)); Color(ImGuiCol.FrameBgActive, White);
            Color(ImGuiCol.Button, Inset); Color(ImGuiCol.ButtonHovered, Hex(0xDED0BB)); Color(ImGuiCol.ButtonActive, Hex(0xD1BFA9));
            Color(ImGuiCol.Header, RoseTint); Color(ImGuiCol.HeaderHovered, Hex(0xE2CBCF)); Color(ImGuiCol.HeaderActive, RoseTint);
            Color(ImGuiCol.CheckMark, Rose); Color(ImGuiCol.SliderGrab, Rose); Color(ImGuiCol.SliderGrabActive, RoseHover);
            Color(ImGuiCol.Separator, Line); Color(ImGuiCol.SeparatorHovered, Brass); Color(ImGuiCol.SeparatorActive, Rose);
            Color(ImGuiCol.ScrollbarBg, Paper); Color(ImGuiCol.ScrollbarGrab, Hex(0xC3B399));
            Color(ImGuiCol.ScrollbarGrabHovered, Brass); Color(ImGuiCol.ScrollbarGrabActive, Rose);
            Color(ImGuiCol.TextSelectedBg, RoseTint); Color(ImGuiCol.TableHeaderBg, Inset);
            Color(ImGuiCol.TableBorderLight, Line); Color(ImGuiCol.TableBorderStrong, Line);
            Style(ImGuiStyleVar.WindowPadding, V(22, 18)); Style(ImGuiStyleVar.FramePadding, V(11, 8));
            Style(ImGuiStyleVar.ItemSpacing, V(10, 10)); Style(ImGuiStyleVar.ItemInnerSpacing, V(7, 5));
            Style(ImGuiStyleVar.CellPadding, V(8, 7));
            Style(ImGuiStyleVar.WindowRounding, S(10)); Style(ImGuiStyleVar.ChildRounding, S(7));
            Style(ImGuiStyleVar.FrameRounding, S(5)); Style(ImGuiStyleVar.PopupRounding, S(7));
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
    public static void Gap(float size = 6) => ImGui.Dummy(V(0, size));
    public static void Rule() { Gap(2); ImGui.Separator(); Gap(2); }

    public static bool Button(string label, float width = 0, bool primary = false, bool quiet = false)
    {
        using var normal = ImRaii.PushColor(ImGuiCol.Button, primary ? Rose : quiet ? Vector4.Zero : Inset);
        using var hover = ImRaii.PushColor(ImGuiCol.ButtonHovered, primary ? RoseHover : RoseTint);
        using var active = ImRaii.PushColor(ImGuiCol.ButtonActive, primary ? Rose : Inset);
        using var text = ImRaii.PushColor(ImGuiCol.Text, primary ? White : Ink);
        return ImGui.Button(label, new Vector2(width, S(36)));
    }

    public static bool Tab(string label, bool selected)
    {
        using var fill = ImRaii.PushColor(ImGuiCol.Button, selected ? RoseTint : Vector4.Zero);
        using var text = ImRaii.PushColor(ImGuiCol.Text, selected ? Rose : Muted);
        return ImGui.Button(label, V(105, 32));
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
        public PaperPanel(string id, Vector2 size, bool inset = false)
        {
            using var color = ImRaii.PushColor(ImGuiCol.ChildBg, inset ? Inset : Panel);
            using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, V(18, 16));
            var scope = ImRaii.Child(id, size, true);
            child = scope;
            Success = scope.Success;
        }
        public void Dispose() => child.Dispose();
    }
}
