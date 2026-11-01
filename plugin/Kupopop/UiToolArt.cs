using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Kupopop;

/// <summary>Compact cartoon tools with flat colour and a consistent black outline.</summary>
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
        var mark = new Mark(draw, origin);
        switch (tool)
        {
            case "pencil": mark.Pencil(); break;
            case "fill": mark.Bucket(); break;
            case "eraser": mark.Eraser(); break;
            case "shapes": mark.Shapes(); break;
        }
        return clicked;
    }

    private static uint Rgb(uint value) => 0xff000000u | (value & 0xff) << 16 | (value & 0xff00) | value >> 16;
    private const uint Ink = 0xff151515;
    private static readonly uint Yellow = Rgb(0xFFBA35), Pink = Rgb(0xF386AF), Blue = Rgb(0x70D2EB),
        Paper = Rgb(0xFFF7E8), Wood = Rgb(0xF1D0A0), Paint = Rgb(0xA986E8), Shine = Rgb(0xFFF1AA);

    private readonly struct Mark(ImDrawListPtr draw, Vector2 origin)
    {
        private Vector2 P(float x, float y) => origin + UiTheme.V(x, y);
        private float Stroke => UiTheme.S(2);
        private uint Halo => UiTheme.C(UiPresentation.Alpha(UiTheme.White, 0.55f));
        private void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            // A narrow outer keyline keeps black edges legible on dark paper.
            if (UiTheme.DarkMode) draw.AddQuad(a, b, c, d, Halo, Stroke + UiTheme.S(0.8f));
            draw.AddQuad(a, b, c, d, Ink, Stroke);
        }
        private void Line(float x, float y, float a, float b, uint colour, float width = 2) =>
            draw.AddLine(P(x, y), P(a, b), colour, UiTheme.S(width));

        public void Pencil()
        {
            draw.AddTriangleFilled(P(7, 18), P(14, 22), P(4, 25), Wood);
            draw.AddTriangle(P(7, 18), P(14, 22), P(4, 25), Ink, Stroke);
            draw.AddQuadFilled(P(7, 18), P(16, 3.5f), P(23, 7.5f), P(14, 22), Yellow);
            Quad(P(7, 18), P(16, 3.5f), P(23, 7.5f), P(14, 22));
            Line(10, 16.5f, 16.5f, 6, Shine, 1.4f);
            Line(12.2f, 18, 19, 7, Ink, 1.2f);
            draw.AddQuadFilled(P(16, 3.5f), P(17.5f, 1.5f), P(24.5f, 5.5f), P(23, 7.5f), Pink);
            Quad(P(16, 3.5f), P(17.5f, 1.5f), P(24.5f, 5.5f), P(23, 7.5f));
            draw.AddTriangleFilled(P(4, 25), P(5.5f, 21), P(8, 23.5f), Ink);
        }

        public void Eraser()
        {
            draw.AddQuadFilled(P(4.5f, 16), P(15, 5), P(24, 12), P(13.5f, 23), Pink);
            draw.AddQuadFilled(P(4.5f, 16), P(10, 10.3f), P(19, 17.3f), P(13.5f, 23), Paper);
            Quad(P(4.5f, 16), P(15, 5), P(24, 12), P(13.5f, 23));
            Line(10, 10.3f, 19, 17.3f, Ink);
        }

        public void Bucket()
        {
            draw.AddBezierCubic(P(7, 10), P(3, 0), P(25, 0), P(21, 10), Ink, Stroke);
            draw.AddQuadFilled(P(6, 10), P(22, 10), P(19, 23), P(9, 23), Blue);
            Quad(P(6, 10), P(22, 10), P(19, 23), P(9, 23));
            draw.AddRectFilled(P(5.5f, 8.5f), P(22.5f, 12), Paper, UiTheme.S(1.7f));
            draw.AddRect(P(5.5f, 8.5f), P(22.5f, 12), Ink, UiTheme.S(1.7f), 0, Stroke);
            Line(9.5f, 14.5f, 10.5f, 19, Paper, 1.5f);
            draw.AddTriangleFilled(P(23, 16), P(20.5f, 21.5f), P(25.5f, 21.5f), Paint);
            draw.AddCircleFilled(P(23, 22), UiTheme.S(2.6f), Paint, 24);
            draw.AddBezierCubic(P(23, 16), P(21, 19), P(18.5f, 24.5f), P(23, 24.5f), Ink, Stroke);
            draw.AddBezierCubic(P(23, 24.5f), P(27.5f, 24.5f), P(25, 19), P(23, 16), Ink, Stroke);
        }

        public void Shapes()
        {
            draw.AddRectFilled(P(4, 5), P(13, 14), Pink, UiTheme.S(1));
            if (UiTheme.DarkMode) draw.AddRect(P(4, 5), P(13, 14), Halo, UiTheme.S(1), 0, Stroke + UiTheme.S(0.8f));
            draw.AddRect(P(4, 5), P(13, 14), Ink, UiTheme.S(1), 0, Stroke);
            draw.AddTriangleFilled(P(20, 4.5f), P(25.5f, 14.5f), P(14.5f, 14.5f), Yellow);
            if (UiTheme.DarkMode) draw.AddTriangle(P(20, 4.5f), P(25.5f, 14.5f), P(14.5f, 14.5f), Halo, Stroke + UiTheme.S(0.8f));
            draw.AddTriangle(P(20, 4.5f), P(25.5f, 14.5f), P(14.5f, 14.5f), Ink, Stroke);
            draw.AddCircleFilled(P(13.5f, 21), UiTheme.S(5), Blue, 32);
            if (UiTheme.DarkMode) draw.AddCircle(P(13.5f, 21), UiTheme.S(5), Halo, 32, Stroke + UiTheme.S(0.8f));
            draw.AddCircle(P(13.5f, 21), UiTheme.S(5), Ink, 32, Stroke);
        }
    }
}
