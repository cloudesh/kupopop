namespace Kupopop.Shared;

public static class Palette
{
    public const byte Transparent = 16;
    // RGBA, little-endian compatible with ImGui packed colors. Index zero is canvas white.
    public static readonly uint[] Colors =
    [
        0xFFFFFFFF, 0xFF222222, 0xFF6A6A6A, 0xFFC4C4C4,
        0xFF4946E8, 0xFF3766F2, 0xFF3AA3FF, 0xFF56DDF9,
        0xFF60BE5B, 0xFF5A7C2A, 0xFFD8BD43, 0xFFE58B41,
        0xFFD8578A, 0xFFAC54D6, 0xFF95625B, 0xFF82B3E8,
        0x00000000,
        0xFF303065, 0xFF5860B8, 0xFF85A7DC, 0xFF89C9F2,
        0xFF4F7A94, 0xFF486B54, 0xFF9ACA87, 0xFFCAF0BB,
        0xFF756E32, 0xFFB59A63, 0xFFF0CCA0, 0xFFE9E0CB,
        0xFF6E3D4F, 0xFFAE789E, 0xFFE2B6D0, 0xFFE9D8EB,
    ];
    public static bool Valid(byte color, bool transparency) => color < Colors.Length && (transparency || color != Transparent);
    public static readonly byte[] Opaque = Enumerable.Range(0, Colors.Length).Where(i => i != Transparent).Select(i => (byte)i).ToArray();
}

public sealed class PixelCanvas
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; private set; }
    public int Revision { get; private set; }
    public bool Transparency { get; }

    public PixelCanvas(int width = Protocol.CanvasWidth, int height = Protocol.CanvasHeight, bool transparency = false)
    {
        Width = width;
        Height = height;
        Pixels = new byte[width * height];
        Transparency = transparency;
        if (transparency) Array.Fill(Pixels, Palette.Transparent);
    }

    public void Load(byte[] pixels)
    {
        if (pixels.Length != Pixels.Length || pixels.Any(p => !Palette.Valid(p, Transparency)))
            throw new InvalidDataException("Invalid canvas.");
        Pixels = (byte[])pixels.Clone();
        Revision++;
    }

    public void Clear() { Array.Fill(Pixels, Transparency ? Palette.Transparent : (byte)0); Revision++; }

    public bool Valid(DrawOperation op) => DrawingTools.Valid(op.Tool)
        && Palette.Valid(op.Color, Transparency) && op.Size is >= 1 and <= 20
        && Inside(op.X1, op.Y1) && Inside(op.X2, op.Y2);

    public bool Apply(DrawOperation op)
    {
        if (!Valid(op)) return false;
        op = DrawingTools.Constrain(op);
        byte color = op.Tool == "eraser" ? (Transparency ? Palette.Transparent : (byte)0) : op.Color;
        if (op.Tool == "fill") Fill(op.X2, op.Y2, color);
        else if (op.Tool is "rectangle" or "square")
        {
            Stroke(op.X1, op.Y1, op.X2, op.Y1, color, op.Size);
            Stroke(op.X2, op.Y1, op.X2, op.Y2, color, op.Size);
            Stroke(op.X2, op.Y2, op.X1, op.Y2, color, op.Size);
            Stroke(op.X1, op.Y2, op.X1, op.Y1, color, op.Size);
        }
        else if (op.Tool is "ellipse" or "circle") Ellipse(op, color);
        else Stroke(op.X1, op.Y1, op.X2, op.Y2, color, op.Size);
        Revision++;
        return true;
    }

    private void Stroke(int x, int y, int endX, int endY, byte color, int size)
    {
        int dx = Math.Abs(endX - x), dy = -Math.Abs(endY - y);
        int sx = x < endX ? 1 : -1, sy = y < endY ? 1 : -1, error = dx + dy;
        while (true)
        {
            Dot(x, y, color, size);
            if (x == endX && y == endY) break;
            int twice = 2 * error;
            if (twice >= dy) { error += dy; x += sx; }
            if (twice <= dx) { error += dx; y += sy; }
        }
    }

    private void Ellipse(DrawOperation op, byte color)
    {
        int left = Math.Min(op.X1, op.X2), right = Math.Max(op.X1, op.X2);
        int top = Math.Min(op.Y1, op.Y2), bottom = Math.Max(op.Y1, op.Y2);
        double rx = (right - left) / 2.0, ry = (bottom - top) / 2.0;
        double cx = (left + right) / 2.0, cy = (top + bottom) / 2.0;
        if (rx == 0 || ry == 0) { Stroke(left, top, right, bottom, color, op.Size); return; }
        for (int x = left; x <= right; x++)
        {
            double distance = ry * Math.Sqrt(Math.Max(0, 1 - Math.Pow((x - cx) / rx, 2)));
            Dot(x, (int)Math.Round(cy - distance), color, op.Size); Dot(x, (int)Math.Round(cy + distance), color, op.Size);
        }
        for (int y = top; y <= bottom; y++)
        {
            double distance = rx * Math.Sqrt(Math.Max(0, 1 - Math.Pow((y - cy) / ry, 2)));
            Dot((int)Math.Round(cx - distance), y, color, op.Size); Dot((int)Math.Round(cx + distance), y, color, op.Size);
        }
    }

    public byte[] Rgba()
    {
        var result = new byte[Pixels.Length * 4];
        for (int i = 0; i < Pixels.Length; i++)
        {
            uint c = Palette.Colors[Pixels[i]];
            result[i * 4] = (byte)c;
            result[i * 4 + 1] = (byte)(c >> 8);
            result[i * 4 + 2] = (byte)(c >> 16);
            result[i * 4 + 3] = (byte)(c >> 24);
        }
        return result;
    }

    private bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
    private void Dot(int x, int y, byte color, int size)
    {
        int r = size / 2;
        for (int yy = y - r; yy <= y + r; yy++)
            for (int xx = x - r; xx <= x + r; xx++)
                if (Inside(xx, yy) && (xx - x) * (xx - x) + (yy - y) * (yy - y) <= r * r + 1)
                    Pixels[yy * Width + xx] = color;
    }

    private void Fill(int x, int y, byte color)
    {
        int start = y * Width + x;
        byte old = Pixels[start];
        if (old == color) return;
        var queue = new Queue<int>();
        Pixels[start] = color;
        queue.Enqueue(start);
        while (queue.TryDequeue(out int p))
        {
            int px = p % Width, py = p / Width;
            if (px > 0) Visit(p - 1);
            if (px + 1 < Width) Visit(p + 1);
            if (py > 0) Visit(p - Width);
            if (py + 1 < Height) Visit(p + Width);
        }
        void Visit(int p)
        {
            if (Pixels[p] != old) return;
            Pixels[p] = color;
            queue.Enqueue(p);
        }
    }
}

public static class AvatarGenerator
{
    // Track the painted silhouette rather than flood-filling white: one-pixel
    // sketch outlines can have gaps, while white face/ear/highlight pixels must
    // remain opaque. Saved custom avatars never pass through this conversion.
    public static byte[] Rgba(int seed)
    {
        int size = Protocol.AvatarSize;
        var canvas = new PixelCanvas(size, size, transparency: true);
        canvas.Load(Generate(seed, out var silhouette));
        byte[] rgba = canvas.Rgba();
        for (int p = 0; p < silhouette.Length; p++)
            if (silhouette[p] == 0) rgba[p * 4 + 3] = 0;
        return rgba;
    }

    public static byte[] Generate(int seed) => Generate(seed, out _);

    private static byte[] Generate(int seed, out byte[] silhouette)
    {
        var canvas = new PixelCanvas(Protocol.AvatarSize, Protocol.AvatarSize);
        var mask = new PixelCanvas(Protocol.AvatarSize, Protocol.AvatarSize);
        var random = new Random(seed);
        byte accent = new byte[] { 4, 6, 7, 12, 13, 15 }[random.Next(6)];
        int expression = random.Next(4), eyeY = random.Next(34, 38), pomX = random.Next(35, 45);
        // White paper, pencil outlines, pointed ears and a colored pom. These are
        // deterministic native avatar shapes, editable with the ordinary tools.
        Triangle(11, 16, 13, 39, 29, 28, 2);
        Triangle(53, 16, 51, 39, 35, 28, 2);
        Triangle(14, 20, 16, 34, 25, 28, 0);
        Triangle(50, 20, 48, 34, 39, 28, 0);
        Triangle(16, 23, 17, 31, 23, 28, 15);
        Triangle(48, 23, 47, 31, 41, 28, 15);
        Ellipse(32, 42, 24, 19, 2);
        Ellipse(32, 41, 23, 18, 0);
        Line(31, 24, 33, 18, 2);
        Line(33, 18, pomX, 13, 2);
        Ellipse(pomX, 9, 7, 7, 2);
        Ellipse(pomX, 9, 6, 6, accent);
        Ellipse(pomX - 2, 7, 2, 2, 0);
        if (expression == 0 || expression == 3)
        {
            Line(17, eyeY + 2, 22, eyeY - 1, 1); Line(22, eyeY - 1, 26, eyeY + 2, 1);
        }
        else Line(22, eyeY, 22, eyeY + 3, 1, 3);
        if (expression == 0 || expression == 2)
        { Line(38, eyeY + 2, 42, eyeY - 1, 1); Line(42, eyeY - 1, 47, eyeY + 2, 1); }
        else Line(42, eyeY, 42, eyeY + 3, 1, 3);
        Ellipse(32, 44, 4, 3, 15);
        Line(32, 47, 28, 50, 2); Line(32, 47, 36, 50, 2);
        Line(9, 42, 16, 44, 3); Line(48, 44, 55, 42, 3);
        if (random.Next(2) == 0) { Ellipse(17, 46, 3, 1, 15); Ellipse(47, 46, 3, 1, 15); }
        for (int i = 0; i < canvas.Pixels.Length; i++)
            if (mask.Pixels[i] == 0) canvas.Pixels[i] = Palette.Transparent;
        silhouette = mask.Pixels;
        return canvas.Pixels;

        void Line(int x1, int y1, int x2, int y2, byte color, int size = 1)
        {
            canvas.Apply(new("pencil", x1, y1, x2, y2, color, size));
            mask.Apply(new("pencil", x1, y1, x2, y2, 1, size));
        }
        void Ellipse(int cx, int cy, int rx, int ry, byte color)
        {
            for (int y = Math.Max(0, cy - ry); y <= Math.Min(63, cy + ry); y++)
                for (int x = Math.Max(0, cx - rx); x <= Math.Min(63, cx + rx); x++)
                    if ((x - cx) * (x - cx) * ry * ry + (y - cy) * (y - cy) * rx * rx <= rx * rx * ry * ry)
                    { canvas.Pixels[y * 64 + x] = color; mask.Pixels[y * 64 + x] = 1; }
        }
        void Triangle(int ax, int ay, int bx, int by, int cx, int cy, byte color)
        {
            int Cross(int px, int py, int x1, int y1, int x2, int y2) => (px - x2) * (y1 - y2) - (x1 - x2) * (py - y2);
            for (int y = Math.Min(ay, Math.Min(by, cy)); y <= Math.Max(ay, Math.Max(by, cy)); y++)
                for (int x = Math.Min(ax, Math.Min(bx, cx)); x <= Math.Max(ax, Math.Max(bx, cx)); x++)
                {
                    int a = Cross(x, y, ax, ay, bx, by), b = Cross(x, y, bx, by, cx, cy), c = Cross(x, y, cx, cy, ax, ay);
                    if (!((a < 0 || b < 0 || c < 0) && (a > 0 || b > 0 || c > 0)))
                    { canvas.Pixels[y * 64 + x] = color; mask.Pixels[y * 64 + x] = 1; }
                }
        }
    }
}
