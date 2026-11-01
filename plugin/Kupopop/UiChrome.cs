using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Kupopop;

public static class UiChrome
{
    public static void CanvasTarget(string id, Vector2 size, bool writable)
    {
        // Read-only sheets must not claim mouse input before their overlaid
        // vote buttons. Only the artist/editor needs an active drawing target.
        if (writable) ImGui.InvisibleButton(id, size);
        else ImGui.Dummy(size);
    }

    public static void ResizeCorner(Vector2 min, Vector2 max, bool locked)
    {
        var draw = ImGui.GetWindowDrawList();
        bool hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows) && ImGui.IsMouseHoveringRect(max - UiTheme.V(24, 24), max);
        var color = locked ? UiTheme.Muted : hovered ? UiTheme.Rose : UiTheme.Brass;
        color.W = locked ? 0.35f : 0.8f;
        draw.PushClipRect(min + Vector2.One, max - Vector2.One, false);
        for (int i = 0; i < 3; i++)
        {
            float length = UiTheme.S(5 + i * 5);
            draw.AddLine(max - new Vector2(UiTheme.S(5) + length, UiTheme.S(5)),
                max - new Vector2(UiTheme.S(5), UiTheme.S(5) + length), UiTheme.C(color), UiTheme.S(1.5f));
        }
        draw.PopClipRect();
        if (hovered)
        {
            if (!locked) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNwse);
            UiTheme.Tooltip(locked ? "Unpin to resize." : "Drag to resize.");
        }
    }

    public static void Footer(Vector2 min, Vector2 max, string version, bool locked)
    {
        var size = ImGui.CalcTextSize(version) * 0.8f;
        ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.8f,
            max - new Vector2(UiTheme.S(34) + size.X, UiTheme.S(17) + size.Y), UiTheme.C(UiTheme.Muted), version);
        ResizeCorner(min, max, locked);
    }
}
