using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Kupopop;

/// <summary>Native notebook materials and little ink drawings. No raster art is generated.</summary>
public static class UiStationery
{
    public static void Book(Vector2 min, Vector2 max, float titleHeight)
    {
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(new Vector2(min.X + 1, min.Y + titleHeight), max - Vector2.One, false);
        min += UiTheme.V(6, 0) + new Vector2(0, titleHeight);
        max -= UiTheme.V(5, 5);
        draw.AddRectFilled(min + UiTheme.V(4, 5), max, UiTheme.C(UiTheme.Inset), UiTheme.S(3));
        draw.AddRect(min + UiTheme.V(4, 5), max, UiTheme.C(UiTheme.Brass), UiTheme.S(3));
        for (int sheet = 3; sheet >= 1; sheet--)
        {
            var offset = UiTheme.V(sheet * 2, sheet * 2);
            draw.AddRectFilled(min + offset, max - UiTheme.V(6 - sheet * 2, 6 - sheet * 2), UiTheme.C(sheet % 2 == 0 ? UiTheme.Panel : UiTheme.Paper), UiTheme.S(2));
            draw.AddRect(min + offset, max - UiTheme.V(6 - sheet * 2, 6 - sheet * 2), UiTheme.C(UiTheme.Line), UiTheme.S(2));
        }
        var pageMax = max - UiTheme.V(7, 7);
        draw.AddRectFilled(min, pageMax, UiTheme.C(UiTheme.Paper), UiTheme.S(2));
        UiTheme.PaperGrain(min, pageMax);
        draw.AddLine(min + UiTheme.V(16, 4), new Vector2(min.X + UiTheme.S(16), pageMax.Y - UiTheme.S(4)), UiTheme.C(UiTheme.Line));
        var margin = UiTheme.Rose; margin.W = 0.15f;
        draw.AddLine(min + UiTheme.V(21, 6), new Vector2(min.X + UiTheme.S(21), pageMax.Y - UiTheme.S(6)), UiTheme.C(margin));
        for (float y = min.Y + UiTheme.S(38); y < pageMax.Y - UiTheme.S(18); y += UiTheme.S(52))
        {
            draw.AddCircleFilled(new Vector2(min.X + UiTheme.S(9), y), UiTheme.S(3), UiTheme.C(UiTheme.Brass));
            draw.AddLine(new Vector2(min.X - UiTheme.S(3), y), new Vector2(min.X + UiTheme.S(9), y), UiTheme.C(UiTheme.Ink), UiTheme.S(2));
            draw.AddLine(new Vector2(min.X - UiTheme.S(3), y - UiTheme.S(1)), new Vector2(min.X + UiTheme.S(9), y - UiTheme.S(1)), UiTheme.C(UiTheme.Line));
        }
        // Exposed page edges on the right/bottom, with short irregular leaf marks.
        for (float y = min.Y + UiTheme.S(20); y < pageMax.Y; y += UiTheme.S(17))
            draw.AddLine(new Vector2(pageMax.X + UiTheme.S(2), y), new Vector2(pageMax.X + UiTheme.S(5), y + UiTheme.S(2)), UiTheme.C(UiTheme.Line));
        draw.PopClipRect();
    }

    public static void Tape(Vector2 top, float width, bool rose = false)
    {
        var draw = ImGui.GetWindowDrawList();
        var tint = rose ? UiTheme.RoseTint : UiTheme.Tape;
        tint.W = 0.85f;
        var a = top + UiTheme.V(0, 2);
        var b = top + new Vector2(width, 0);
        var c = b + UiTheme.V(-2, 12);
        var d = a + UiTheme.V(2, 12);
        draw.AddQuadFilled(a, b, c, d, UiTheme.C(tint));
        var ink = UiTheme.Brass; ink.W = 0.22f;
        for (float x = UiTheme.S(6); x < width - UiTheme.S(3); x += UiTheme.S(9))
            draw.AddLine(top + new Vector2(x, UiTheme.S(3)), top + new Vector2(x - UiTheme.S(2), UiTheme.S(11)), UiTheme.C(ink));
    }

    public static void PageMark(string label)
    {
        var start = ImGui.GetCursorScreenPos();
        ImGui.TextColored(UiTheme.Rose, label);
        var end = ImGui.GetItemRectMax();
        ImGui.GetWindowDrawList().AddLine(start + new Vector2(0, ImGui.GetTextLineHeight() + UiTheme.S(2)), end + UiTheme.V(5, 2), UiTheme.C(UiTheme.Rose), UiTheme.S(1.4f));
        UiTheme.Gap(3);
    }

    public static void Moogle(Vector2 center, float radius, float angle = 0)
    {
        var draw = ImGui.GetWindowDrawList();
        Vector2 P(float x, float y) => center + new Vector2(x, y) * radius;
        uint ink = UiTheme.C(UiTheme.Ink), cream = UiTheme.C(UiTheme.White), pink = UiTheme.C(UiTheme.RoseTint);
        draw.AddTriangleFilled(P(-0.85f, -0.2f), P(-0.85f, -1.05f), P(-0.2f, -0.65f), cream);
        draw.AddTriangleFilled(P(0.85f, -0.2f), P(0.85f, -1.05f), P(0.2f, -0.65f), cream);
        draw.AddTriangle(P(-0.85f, -0.2f), P(-0.85f, -1.05f), P(-0.2f, -0.65f), ink, UiTheme.S(1));
        draw.AddTriangle(P(0.85f, -0.2f), P(0.85f, -1.05f), P(0.2f, -0.65f), ink, UiTheme.S(1));
        draw.AddTriangleFilled(P(-0.71f, -0.44f), P(-0.73f, -0.8f), P(-0.4f, -0.59f), pink);
        draw.AddTriangleFilled(P(0.71f, -0.44f), P(0.73f, -0.8f), P(0.4f, -0.59f), pink);
        Ellipse(draw, center, new Vector2(radius, radius * 0.8f), cream, true);
        Ellipse(draw, center, new Vector2(radius, radius * 0.8f), ink, false);
        // Happy closed eyes and a rose button nose.
        draw.AddLine(P(-0.55f, -0.1f), P(-0.36f, -0.22f), ink, UiTheme.S(1.4f));
        draw.AddLine(P(-0.36f, -0.22f), P(-0.17f, -0.08f), ink, UiTheme.S(1.4f));
        draw.AddLine(P(0.17f, -0.08f), P(0.36f, -0.22f), ink, UiTheme.S(1.4f));
        draw.AddLine(P(0.36f, -0.22f), P(0.55f, -0.1f), ink, UiTheme.S(1.4f));
        Ellipse(draw, P(0, 0.18f), new Vector2(radius * 0.18f, radius * 0.13f), UiTheme.C(UiTheme.Rose), true);
        draw.AddLine(P(0, 0.32f), P(-0.15f, 0.43f), ink, UiTheme.S(1));
        draw.AddLine(P(0, 0.32f), P(0.15f, 0.43f), ink, UiTheme.S(1));
        var root = P(0, -0.7f);
        var pom = root + new Vector2(MathF.Sin(angle) * radius * 0.85f, -MathF.Cos(angle) * radius * 0.85f);
        draw.AddBezierCubic(root, root + new Vector2(radius * 0.15f, -radius * 0.45f), pom + new Vector2(-radius * 0.2f, radius * 0.3f), pom, ink, UiTheme.S(1.2f));
        draw.AddCircleFilled(pom + UiTheme.V(1, 1), radius * 0.35f, UiTheme.C(UiTheme.Line));
        draw.AddCircleFilled(pom, radius * 0.35f, UiTheme.C(UiTheme.RoseHover));
        draw.AddCircle(pom, radius * 0.35f, UiTheme.C(UiTheme.Rose), 0, UiTheme.S(1));
        draw.AddCircleFilled(pom + new Vector2(-radius * 0.1f, -radius * 0.1f), radius * 0.09f, UiTheme.C(UiTheme.RoseTint));
    }

    private static void Ellipse(ImDrawListPtr draw, Vector2 center, Vector2 radii, uint color, bool fill)
    {
        draw.PathClear();
        for (int i = 0; i < 32; i++)
        {
            float angle = i * (MathF.Tau / 32);
            draw.PathLineTo(center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radii);
        }
        if (fill) draw.PathFillConvex(color);
        else draw.PathStroke(color, ImDrawFlags.Closed, UiTheme.S(1.1f));
    }
}
