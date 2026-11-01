using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Kupopop;

/// <summary>Small stationery illustrations, drawn directly at the current UI scale.</summary>
internal static class UiToolArt
{
    public static bool Button(string tool, bool selected)
    {
        var min = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.InvisibleButton("tool-" + tool, UiTheme.V(28, 28));
        var draw = ImGui.GetWindowDrawList();
        Vector2 P(float x, float y) => min + UiTheme.V(x, y);
        uint ink = UiTheme.C(UiTheme.Ink), accent = UiTheme.C(UiTheme.Rose);
        draw.AddRectFilled(min, P(28, 28), UiTheme.C(selected ? UiTheme.RoseTint : ImGui.IsItemHovered() ? UiTheme.Panel : UiTheme.Inset), UiTheme.S(3));
        if (tool == "pencil")
        {
            draw.AddQuadFilled(P(8, 19), P(18, 5), P(23, 9), P(13, 23), UiTheme.C(UiTheme.Tape));
            draw.AddLine(P(10, 20), P(20, 7), ink, UiTheme.S(1));
            draw.AddTriangleFilled(P(8, 19), P(13, 23), P(5, 25), UiTheme.C(UiTheme.White));
            draw.AddTriangleFilled(P(5, 25), P(7, 21), P(9, 24), ink);
            draw.AddQuadFilled(P(18, 5), P(20, 3), P(25, 7), P(23, 9), accent);
        }
        else if (tool == "eraser")
        {
            draw.AddQuadFilled(P(5, 17), P(15, 6), P(24, 13), P(14, 24), accent);
            draw.AddQuadFilled(P(5, 17), P(10, 12), P(19, 19), P(14, 24), UiTheme.C(UiTheme.White));
            draw.AddLine(P(5, 25), P(24, 25), ink, UiTheme.S(1));
        }
        else if (tool == "fill")
        {
            draw.AddQuadFilled(P(5, 13), P(13, 5), P(22, 14), P(14, 22), UiTheme.C(UiTheme.Muted));
            draw.AddQuadFilled(P(7, 13), P(13, 7), P(19, 13), P(13, 19), UiTheme.C(UiTheme.Panel));
            draw.AddLine(P(8, 5), P(19, 16), ink, UiTheme.S(2));
            draw.AddTriangleFilled(P(23, 16), P(20, 22), P(26, 22), accent);
            draw.AddCircleFilled(P(23, 22), UiTheme.S(3), accent);
        }
        else
        {
            // Overlapping square/triangle/circle reads as a shape collection.
            draw.AddRectFilled(P(5, 5), P(15, 15), UiTheme.C(UiTheme.Muted), UiTheme.S(1));
            draw.AddTriangleFilled(P(17, 5), P(25, 17), P(10, 17), accent);
            draw.AddCircleFilled(P(12, 20), UiTheme.S(6), UiTheme.C(UiTheme.Panel));
            draw.AddCircle(P(12, 20), UiTheme.S(6), ink, 20, UiTheme.S(1));
        }
        return clicked;
    }
}
