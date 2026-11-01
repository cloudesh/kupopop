using System.Numerics;

namespace Kupopop;

public static class UiWindowGeometry
{
    public const float Aspect = 1.5f;
    public static readonly Vector2 DefaultSize = new(990, 660);

    public static (Vector2 Minimum, Vector2 Maximum) Bounds(Vector2 display)
    {
        float height = Math.Max(240, Math.Min(display.Y - 40, (display.X - 40) / Aspect));
        var maximum = new Vector2(height * Aspect, height);
        float minimumHeight = Math.Min(560, height);
        return (new(minimumHeight * Aspect, minimumHeight), maximum);
    }

    public static Vector2 Constrain(Vector2 desired, Vector2 current, Vector2 minimum, Vector2 maximum)
    {
        var change = Vector2.Abs(desired - current);
        float height = change.X >= change.Y * Aspect ? desired.X / Aspect : desired.Y;
        height = Math.Clamp(height, minimum.Y, maximum.Y);
        return new(MathF.Round(height * Aspect), MathF.Round(height));
    }
}
