using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Kupopop;

/// <summary>Native notebook materials and little ink drawings. No raster art is generated.</summary>
public static class UiStationery
{
    public static void PaperMaterial(ImTextureID texture, Vector2 min, Vector2 max, Vector4? tint = null)
    {
        var material = tint ?? (UiTheme.DarkMode ? UiTheme.Paper : Vector4.One);
        material.W = UiTheme.DarkMode ? 0.10f : tint.HasValue ? 0.18f : 0.32f;
        // One flat interior below the game's divider, without repeated shading.
        ImGui.GetWindowDrawList().AddImage(texture, min, max, new Vector2(0.08f, 0.39f), new Vector2(0.94f, 0.83f), UiTheme.C(material));
    }

    public static void Book(Vector2 min, Vector2 max, float titleHeight)
    {
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(new Vector2(min.X + 1, min.Y + titleHeight), max - Vector2.One, false);
        min += UiTheme.V(18, 11) + new Vector2(0, titleHeight);
        max -= UiTheme.V(18, 12);
        // Opposing rotations expose leaves on every side, inside the transparent
        // window margins. The uppermost page stays square for precise controls.
        PaperLeaf(min - UiTheme.V(2, 1), max + UiTheme.V(2, 1), -0.85f, UiTheme.V(-2, 1), UiTheme.Inset);
        PaperLeaf(min, max, 0.65f, UiTheme.V(2, -1), UiTheme.Panel);
        PaperLeaf(min, max, -0.32f, UiTheme.V(-1, 2), UiTheme.Paper);
        var pageMax = max;
        draw.AddRectFilled(min, pageMax, UiTheme.C(UiTheme.Paper), UiTheme.S(2));
        UiTheme.PaperGrain(min, pageMax);
        PencilEdges(min, pageMax);
        draw.AddLine(min + UiTheme.V(8, 4), new Vector2(min.X + UiTheme.S(8), pageMax.Y - UiTheme.S(4)), UiTheme.C(UiTheme.Line));
        var margin = UiTheme.Rose; margin.W = 0.15f;
        draw.AddLine(min + UiTheme.V(10, 6), new Vector2(min.X + UiTheme.S(10), pageMax.Y - UiTheme.S(6)), UiTheme.C(margin));
        for (float y = min.Y + UiTheme.S(38); y < pageMax.Y - UiTheme.S(18); y += UiTheme.S(52))
        {
            draw.AddCircleFilled(new Vector2(min.X + UiTheme.S(3), y), UiTheme.S(2), UiTheme.C(UiTheme.Brass));
            draw.AddLine(new Vector2(min.X - UiTheme.S(5), y), new Vector2(min.X + UiTheme.S(3), y), UiTheme.C(UiTheme.Ink), UiTheme.S(2));
            draw.AddLine(new Vector2(min.X - UiTheme.S(5), y - UiTheme.S(1)), new Vector2(min.X + UiTheme.S(3), y - UiTheme.S(1)), UiTheme.C(UiTheme.Line));
        }
        draw.PopClipRect();
    }

    private static void PencilEdges(Vector2 min, Vector2 max)
    {
        var draw = ImGui.GetWindowDrawList();
        var ink = UiTheme.Ink; ink.W = 0.12f;
        // Stationary, slightly uneven pencil marks sit inside the paper margin.
        // They never alter the rectangular controls or precise drawing surface.
        for (int side = 0; side < 2; side++)
        {
            float y = side == 0 ? min.Y + UiTheme.S(3) : max.Y - UiTheme.S(3);
            for (int i = 0; i < (max.X - min.X - UiTheme.S(14)) / UiTheme.S(23); i++)
            {
                float x = min.X + UiTheme.S(7 + i * 23);
                float wobble = UiTheme.S((i % 3 - 1) * 0.35f);
                draw.AddLine(new Vector2(x, y + wobble), new Vector2(x + UiTheme.S(19), y - wobble), UiTheme.C(ink), UiTheme.S(0.7f));
            }
        }
        var corner = max - UiTheme.V(6, 6);
        var crease = UiTheme.Brass; crease.W = 0.16f;
        draw.AddTriangleFilled(corner, corner - UiTheme.V(13, 0), corner - UiTheme.V(0, 13), UiTheme.C(crease));
        draw.AddLine(corner - UiTheme.V(13, 0), corner - UiTheme.V(0, 13), UiTheme.C(ink), UiTheme.S(0.8f));
    }

    public static void PaperLeaf(Vector2 min, Vector2 max, float degrees, Vector2 offset, Vector4 color)
    {
        var center = (min + max) / 2;
        float angle = degrees * (MathF.PI / 180), cosine = MathF.Cos(angle), sine = MathF.Sin(angle);
        Vector2 Rotate(Vector2 point)
        {
            var p = point - center;
            return center + new Vector2(p.X * cosine - p.Y * sine, p.X * sine + p.Y * cosine) + offset;
        }
        var a = Rotate(min); var b = Rotate(new Vector2(max.X, min.Y));
        var c = Rotate(max); var d = Rotate(new Vector2(min.X, max.Y));
        var draw = ImGui.GetWindowDrawList();
        var shadow = UiTheme.Shadow; shadow.W = 0.12f;
        var drop = UiTheme.V(1, 2);
        draw.AddQuadFilled(a + drop, b + drop, c + drop, d + drop, UiTheme.C(shadow));
        draw.AddQuadFilled(a, b, c, d, UiTheme.C(color));
        draw.AddQuad(a, b, c, d, UiTheme.C(UiTheme.Line), UiTheme.S(1));
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

    public static void SketchNote(Vector2 min, Vector2 max)
    {
        var draw = ImGui.GetWindowDrawList();
        var ink = UiTheme.Ink; ink.W = UiTheme.DarkMode ? 0.12f : 0.17f;
        uint color = UiTheme.C(ink);
        // Short, imperfect graphite strokes stay at the edges, clear of text.
        for (int side = 0; side < 2; side++)
        {
            float y = side == 0 ? min.Y + UiTheme.S(2) : max.Y - UiTheme.S(2);
            for (int i = 0; i < 3; i++)
            {
                float x = min.X + UiTheme.S(6 + i * 9);
                draw.AddBezierCubic(new(x, y), new(x + UiTheme.S(5), y - UiTheme.S(0.8f)),
                    new(x + UiTheme.S(11), y + UiTheme.S(0.8f)), new(x + UiTheme.S(15), y), color, UiTheme.S(0.8f));
            }
        }
        draw.AddLine(new(max.X - UiTheme.S(2), min.Y + UiTheme.S(21)),
            new(max.X - UiTheme.S(1.5f), min.Y + UiTheme.S(48)), color, UiTheme.S(0.7f));
    }

    public static void Paperclip(Vector2 origin)
    {
        var draw = ImGui.GetWindowDrawList();
        Vector2 P(float x, float y) => origin + UiTheme.V(x + y * 0.15f, y);
        uint shadow = UiTheme.C(UiPresentation.Alpha(UiTheme.CanvasInk, 0.18f));
        uint metal = UiTheme.C(UiTheme.DarkMode ? UiTheme.Line : UiTheme.Brass);
        for (int pass = 0; pass < 2; pass++)
        {
            var offset = pass == 0 ? UiTheme.V(1, 1) : Vector2.Zero;
            uint color = pass == 0 ? shadow : metal;
            float width = UiTheme.S(pass == 0 ? 3 : 1.5f);
            draw.AddBezierCubic(P(0, 16) + offset, P(0, 26) + offset, P(11, 26) + offset, P(11, 16) + offset, color, width);
            draw.AddBezierCubic(P(11, 16) + offset, P(11, -9) + offset, P(-4, -9) + offset, P(-4, 4) + offset, color, width);
            draw.AddLine(P(-4, 4) + offset, P(-4, 17) + offset, color, width);
            draw.AddBezierCubic(P(-4, 17) + offset, P(-4, 32) + offset, P(17, 32) + offset, P(17, 17) + offset, color, width);
            draw.AddLine(P(17, 17) + offset, P(17, 1) + offset, color, width);
        }
    }

    /// <summary>The moogle artist mark; also exported unchanged for the installer.</summary>
    public static void Logo(Vector2 center, float radius, float angle = 0, float pomScale = 1)
    {
        Moogle(center, radius, angle, pomScale);
        var draw = ImGui.GetWindowDrawList();
        Vector2 P(float x, float y) => center + new Vector2(x, y) * radius;
        float stroke = Math.Max(UiTheme.S(1), radius * 0.06f);
        uint ink = UiTheme.C(UiTheme.CanvasInk);
        var blush = UiTheme.RoseHover; blush.W = 0.45f;
        draw.AddCircleFilled(P(-0.58f, 0.23f), radius * 0.13f, UiTheme.C(blush), 16);
        draw.AddCircleFilled(P(0.58f, 0.23f), radius * 0.13f, UiTheme.C(blush), 16);
        // A little pencil tucked under one paw makes the drawing-game purpose clear.
        uint yellow = UiTheme.C(new Vector4(0xF5 / 255f, 0xBD / 255f, 0x54 / 255f, 1));
        draw.AddQuadFilled(P(0.65f, 0.55f), P(1.13f, -0.55f), P(1.44f, -0.41f), P(0.96f, 0.69f), yellow);
        draw.AddQuad(P(0.65f, 0.55f), P(1.13f, -0.55f), P(1.44f, -0.41f), P(0.96f, 0.69f), ink, stroke);
        draw.AddLine(P(0.80f, 0.54f), P(1.24f, -0.4f), ink, stroke * 0.6f);
        draw.AddTriangleFilled(P(0.65f, 0.55f), P(0.96f, 0.69f), P(0.61f, 0.94f), UiTheme.C(UiTheme.White));
        draw.AddTriangle(P(0.65f, 0.55f), P(0.96f, 0.69f), P(0.61f, 0.94f), ink, stroke);
        draw.AddLine(P(0.61f, 0.94f), P(0.68f, 0.79f), ink, stroke * 1.4f);
        draw.AddCircleFilled(P(0.69f, 0.38f), radius * 0.21f, UiTheme.C(UiTheme.White), 20);
        draw.AddCircle(P(0.69f, 0.38f), radius * 0.21f, ink, 20, stroke);
        draw.AddLine(P(-1.28f, -0.53f), P(-1.39f, -0.72f), UiTheme.C(UiTheme.Rose), stroke);
        draw.AddLine(P(-1.43f, -0.4f), P(-1.62f, -0.47f), UiTheme.C(UiTheme.Rose), stroke);
    }

    public static void Moogle(Vector2 center, float radius, float angle = 0, float pomScale = 1)
    {
        var draw = ImGui.GetWindowDrawList();
        Vector2 P(float x, float y) => center + new Vector2(x, y) * radius;
        uint ink = UiTheme.C(UiTheme.CanvasInk), cream = UiTheme.C(UiTheme.White), pink = UiTheme.C(UiTheme.RoseTint);
        float stroke = Math.Max(UiTheme.S(1), radius * 0.04f);
        draw.AddTriangleFilled(P(-0.85f, -0.2f), P(-0.85f, -1.05f), P(-0.2f, -0.65f), cream);
        draw.AddTriangleFilled(P(0.85f, -0.2f), P(0.85f, -1.05f), P(0.2f, -0.65f), cream);
        draw.AddTriangle(P(-0.85f, -0.2f), P(-0.85f, -1.05f), P(-0.2f, -0.65f), ink, stroke);
        draw.AddTriangle(P(0.85f, -0.2f), P(0.85f, -1.05f), P(0.2f, -0.65f), ink, stroke);
        draw.AddTriangleFilled(P(-0.71f, -0.44f), P(-0.73f, -0.8f), P(-0.4f, -0.59f), pink);
        draw.AddTriangleFilled(P(0.71f, -0.44f), P(0.73f, -0.8f), P(0.4f, -0.59f), pink);
        Ellipse(draw, center, new Vector2(radius, radius * 0.8f), cream, true);
        Ellipse(draw, center, new Vector2(radius, radius * 0.8f), ink, false);
        // Happy closed eyes and a rose button nose.
        draw.AddLine(P(-0.55f, -0.1f), P(-0.36f, -0.22f), ink, Math.Max(UiTheme.S(1.4f), stroke));
        draw.AddLine(P(-0.36f, -0.22f), P(-0.17f, -0.08f), ink, Math.Max(UiTheme.S(1.4f), stroke));
        draw.AddLine(P(0.17f, -0.08f), P(0.36f, -0.22f), ink, Math.Max(UiTheme.S(1.4f), stroke));
        draw.AddLine(P(0.36f, -0.22f), P(0.55f, -0.1f), ink, Math.Max(UiTheme.S(1.4f), stroke));
        Ellipse(draw, P(0, 0.18f), new Vector2(radius * 0.18f, radius * 0.13f), UiTheme.C(UiTheme.Rose), true);
        draw.AddBezierCubic(P(-0.16f, 0.36f), P(-0.1f, 0.53f), P(0.1f, 0.53f), P(0.16f, 0.36f), ink, stroke);
        var root = P(0, -0.7f);
        float stalkLength = radius * 0.85f * (1 + (pomScale - 1) * 0.5f);
        var pom = root + new Vector2(MathF.Sin(angle), -MathF.Cos(angle)) * stalkLength;
        draw.AddBezierCubic(root, root + new Vector2(radius * 0.15f, -stalkLength * 0.53f), pom + new Vector2(-radius * 0.2f, stalkLength * 0.35f), pom, ink,
            Math.Max(UiTheme.S(1.2f), stroke) * pomScale);
        draw.AddCircleFilled(pom + UiTheme.V(1, 1), radius * 0.35f * pomScale, UiTheme.C(UiTheme.Line));
        draw.AddCircleFilled(pom, radius * 0.35f * pomScale, UiTheme.C(UiTheme.RoseHover));
        draw.AddCircle(pom, radius * 0.35f * pomScale, UiTheme.C(UiTheme.Rose), 0, stroke);
        draw.AddCircleFilled(pom + new Vector2(-radius * 0.1f, -radius * 0.1f) * pomScale, radius * 0.09f * pomScale, UiTheme.C(UiTheme.RoseTint));
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
        else draw.PathStroke(color, ImDrawFlags.Closed, Math.Max(UiTheme.S(1.1f), radii.X * 0.04f));
    }
}
