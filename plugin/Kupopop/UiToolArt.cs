using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Kupopop;

/// <summary>Consistent fine-line stationery marks, rendered sharply at the current UI scale.</summary>
internal static class UiToolArt
{
    public static bool Button(string tool, bool selected)
    {
        var origin = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.InvisibleButton("tool-" + tool, UiTheme.V(28, 28));
        var draw = ImGui.GetWindowDrawList();
        var end = origin + UiTheme.V(28, 28);
        var wash = selected ? UiPresentation.Alpha(UiTheme.RoseTint, 0.85f)
            : ImGui.IsItemHovered() ? UiPresentation.Alpha(UiTheme.Inset, 0.65f)
            : UiPresentation.Alpha(UiTheme.Ink, 0.035f);
        draw.AddRectFilled(origin, end, UiTheme.C(wash), UiTheme.S(5));
        if (selected)
            draw.AddRect(origin + UiTheme.V(0.5f, 0.5f), end - UiTheme.V(0.5f, 0.5f),
                UiTheme.C(UiPresentation.Alpha(UiTheme.Rose, 0.35f)), UiTheme.S(5), 0, UiTheme.S(1));

        var mark = new Mark(draw, origin, selected);
        switch (tool)
        {
            case "pencil": mark.Pencil(); break;
            case "fill": mark.Bucket(); break;
            case "eraser": mark.Eraser(); break;
            case "shapes": mark.Shapes(); break;
        }
        return clicked;
    }

    private readonly struct Mark(ImDrawListPtr draw, Vector2 origin, bool selected)
    {
        private Vector2 P(float x, float y) => origin + UiTheme.V(x, y);
        private uint Ink => UiTheme.C(selected ? UiTheme.Rose : UiTheme.Ink);
        private uint Wash => UiTheme.C(UiPresentation.Alpha(UiTheme.Rose, UiTheme.DarkMode ? 0.12f : 0.07f));
        private float Stroke => UiTheme.S(1.35f);
        private void Line(float ax, float ay, float bx, float by) => draw.AddLine(P(ax, ay), P(bx, by), Ink, Stroke);

        public void Pencil()
        {
            // A long barrel, inset graphite seam, narrow ferrule and cut wood tip.
            draw.AddQuadFilled(P(7, 17), P(17, 7), P(21, 11), P(11, 21), Wash);
            draw.AddQuad(P(7, 17), P(17, 7), P(21, 11), P(11, 21), Ink, Stroke);
            draw.AddTriangle(P(7, 17), P(11, 21), P(4.5f, 23.5f), Ink, Stroke);
            draw.AddTriangleFilled(P(4.5f, 23.5f), P(5.5f, 20.5f), P(7.5f, 22.5f), Ink);
            Line(10, 17, 18.5f, 8.5f);
            draw.AddQuad(P(17, 7), P(19.5f, 4.5f), P(23.5f, 8.5f), P(21, 11), Ink, Stroke);
            Line(16, 8, 20, 12);
        }

        public void Eraser()
        {
            // The sleeve is a light wash; the exposed rubber stays paper-coloured.
            draw.AddQuadFilled(P(10, 11), P(15, 6), P(23, 13), P(18, 18), Wash);
            draw.AddQuad(P(5, 16), P(15, 6), P(23, 13), P(13, 23), Ink, Stroke);
            Line(10, 11, 18, 18);
            Line(13, 23, 22, 23);
            Line(17, 8.5f, 21, 12);
        }

        public void Bucket()
        {
            draw.AddQuadFilled(P(4.5f, 14), P(11, 7.5f), P(20, 16.5f), P(13.5f, 23), Wash);
            draw.AddQuad(P(4.5f, 14), P(11, 7.5f), P(20, 16.5f), P(13.5f, 23), Ink, Stroke);
            // Curved wire handle and a double lip distinguish a paint pail.
            draw.AddBezierCubic(P(7, 11.5f), P(1, 1), P(15, 0.5f), P(15, 11.5f), Ink, Stroke);
            Line(10, 6, 22, 18);
            Line(12, 6, 22, 16);
            draw.AddBezierCubic(P(23, 18.5f), P(20, 22), P(19.5f, 25), P(23, 25), Ink, Stroke);
            draw.AddBezierCubic(P(23, 25), P(26.5f, 25), P(26, 22), P(23, 18.5f), Ink, Stroke);
        }

        public void Shapes()
        {
            draw.AddRect(P(4.5f, 5.5f), P(13.5f, 14.5f), Ink, UiTheme.S(1.2f), 0, Stroke);
            draw.AddTriangle(P(20, 5.5f), P(25, 14.5f), P(15, 14.5f), Ink, Stroke);
            draw.AddCircleFilled(P(13.5f, 21), UiTheme.S(5), Wash);
            draw.AddCircle(P(13.5f, 21), UiTheme.S(5), Ink, 32, Stroke);
        }
    }
}
