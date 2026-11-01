using System.Numerics;

namespace Kupopop;

public readonly record struct UiGameLayout(float Canvas, float Artists, float Chat, float Gap, float Inset, float Width)
{
    public static UiGameLayout Fit(Vector2 available, float scale)
    {
        float artists = 172 * scale, chat = 200 * scale, gap = 8 * scale;
        float canvas = Math.Max(64 * scale, Math.Min(available.Y - 10 * scale,
            available.X - artists - chat - gap * 2 - 9 * scale));
        float width = artists + chat + canvas + gap * 2 + 9 * scale;
        return new(canvas, artists, chat, gap, Math.Max(0, (available.X - width) / 2), width);
    }
}
