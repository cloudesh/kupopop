using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Kupopop;

/// <summary>Original pen-stroke lettering, shared by the book header and cover.</summary>
internal static class UiBrand
{
    // Each path is a continuous stroke in a small, deliberately imperfect alphabet.
    private sealed record Letter(float Width, Vector2[][] Paths, bool Rounded = false);
    private static readonly Dictionary<char, Letter> Letters = new()
    {
        ['K'] = new(10, [[new(1, 1), new(0.5f, 15)], [new(10, 0), new(1, 8), new(10, 15)]]),
        ['u'] = new(9, [[new(1, 5), new(0.7f, 11), new(1.8f, 14), new(4, 15), new(7.5f, 13), new(8, 5), new(8, 15)]], true),
        ['p'] = new(9, [[new(1, 5), new(0.5f, 19)], [new(1, 7), new(4, 5), new(7.5f, 6), new(9, 9), new(8, 12.5f), new(5, 14), new(1, 12.5f)]], true),
        ['o'] = new(9, [[new(5, 5), new(2, 5.5f), new(0.5f, 9), new(1.5f, 13), new(5, 15), new(8, 13.5f), new(9, 9), new(7, 5.5f), new(5, 5)]], true),
        ['S'] = new(10, [[new(9, 2), new(6, 0.5f), new(2, 1), new(0.5f, 4), new(2, 6.5f), new(7, 8), new(9.5f, 10.5f), new(8, 14), new(4, 15), new(0, 13)]], true),
        ['k'] = new(8, [[new(1, 0), new(0.5f, 15)], [new(8, 5), new(1, 10), new(8, 15)]]),
        ['r'] = new(7, [[new(1, 5), new(0.5f, 15)], [new(1, 9), new(3, 6), new(5, 5), new(7, 6)]]),
        ['i'] = new(3, [[new(1.5f, 6), new(1, 15)], [new(1.5f, 1), new(1.6f, 1.2f)]]),
        ['b'] = new(9, [[new(1, 0), new(0.5f, 15)], [new(1, 8), new(4, 5), new(7, 6), new(9, 9.5f), new(8, 13), new(5, 15), new(1, 14)]], true),
        ['l'] = new(4, [[new(1.5f, 0), new(0.7f, 12), new(1.5f, 14.5f), new(4, 15)]], true),
    };
    private const string Name = "Kupo Skribbl";
    private static readonly float Units = Name.Sum(c => c == ' ' ? 6 : Letters[c].Width + 3);
    private static readonly Vector4[] MarkerColors =
    [new(0.84f, 0.48f, 0.60f, 1), new(0.89f, 0.71f, 0.34f, 1), new(0.45f, 0.67f, 0.64f, 1)];

    public static void Wordmark(Vector2 origin, Vector2 bounds, bool cover = false)
    {
        float unit = Math.Min(bounds.X / Units, bounds.Y / 22);
        var draw = ImGui.GetWindowDrawList();
        float x = 1;
        for (int index = 0; index < Name.Length; index++)
        {
            char character = Name[index];
            if (character == ' ') { x += 6; continue; }
            var letter = Letters[character];
            float tilt = (index % 3 - 1) * 0.045f;
            float offset = (index % 4 == 1 ? 0.6f : 0) + 1;
            Vector2 Point(Vector2 p) => origin + new Vector2(x + p.X + (p.Y - 8) * tilt, p.Y + offset) * unit;
            int passes = cover ? 2 : 1;
            for (int pass = 0; pass < passes; pass++)
            {
                var ink = !cover ? UiTheme.Ink : pass == 0 ? UiTheme.CanvasInk : MarkerColors[index % MarkerColors.Length];
                float thickness = unit * (!cover ? 1.35f : pass == 0 ? 2.8f : 1.55f);
                foreach (var path in letter.Paths)
                {
                    for (int i = 1; i < path.Length; i++)
                    {
                        if (!letter.Rounded || path.Length == 2) draw.AddLine(Point(path[i - 1]), Point(path[i]), UiTheme.C(ink), thickness);
                        else
                        {
                            var a = path[i - 1]; var b = path[i];
                            var before = path[Math.Max(0, i - 2)]; var after = path[Math.Min(path.Length - 1, i + 1)];
                            draw.AddBezierCubic(Point(a), Point(a + (b - before) / 6), Point(b - (after - a) / 6), Point(b), UiTheme.C(ink), thickness);
                        }
                    }
                    foreach (var point in path) draw.AddCircleFilled(Point(point), thickness / 2, UiTheme.C(ink), 8);
                }
            }
            x += letter.Width + 3;
        }
        if (cover)
        {
            var color = UiTheme.Rose; color.W = 0.35f;
            var start = origin + new Vector2(0, 21) * unit;
            draw.AddBezierCubic(start, start + new Vector2(bounds.X * 0.3f, -2 * unit),
                start + new Vector2(bounds.X * 0.7f, unit), start + new Vector2(x * unit, -unit), UiTheme.C(color), unit * 0.7f);
        }
    }
}
