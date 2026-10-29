namespace Kupopop.Shared;

public static class Palette
{
    // RGBA, little-endian compatible with ImGui packed colors. Index zero is canvas white.
    public static readonly uint[] Colors =
    [
        0xFFFFFFFF, 0xFF222222, 0xFF6A6A6A, 0xFFC4C4C4,
        0xFF4946E8, 0xFF3766F2, 0xFF3AA3FF, 0xFF56DDF9,
        0xFF60BE5B, 0xFF5A7C2A, 0xFFD8BD43, 0xFFE58B41,
        0xFFD8578A, 0xFFAC54D6, 0xFF95625B, 0xFF82B3E8,
    ];
}

public sealed class PixelCanvas
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; private set; }
    public int Revision { get; private set; }

    public PixelCanvas(int width = Protocol.CanvasWidth, int height = Protocol.CanvasHeight)
    {
        Width = width;
        Height = height;
        Pixels = new byte[width * height];
    }

    public void Load(byte[] pixels)
    {
        if (pixels.Length != Pixels.Length || pixels.Any(p => p >= Palette.Colors.Length))
            throw new InvalidDataException("Invalid canvas.");
        Pixels = (byte[])pixels.Clone();
        Revision++;
    }

    public void Clear() { Array.Clear(Pixels); Revision++; }

    public bool Valid(DrawOperation op) => op.Tool is "pencil" or "eraser" or "fill"
        && op.Color < Palette.Colors.Length && op.Size is >= 1 and <= 20
        && Inside(op.X1, op.Y1) && Inside(op.X2, op.Y2);

    public bool Apply(DrawOperation op)
    {
        if (!Valid(op)) return false;
        byte color = op.Tool == "eraser" ? (byte)0 : op.Color;
        if (op.Tool == "fill") Fill(op.X2, op.Y2, color);
        else
        {
            int x = op.X1, y = op.Y1, dx = Math.Abs(op.X2 - x), dy = -Math.Abs(op.Y2 - y);
            int sx = x < op.X2 ? 1 : -1, sy = y < op.Y2 ? 1 : -1, error = dx + dy;
            while (true)
            {
                Dot(x, y, color, op.Size);
                if (x == op.X2 && y == op.Y2) break;
                int twice = 2 * error;
                if (twice >= dy) { error += dy; x += sx; }
                if (twice <= dx) { error += dx; y += sy; }
            }
        }
        Revision++;
        return true;
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
            result[i * 4 + 3] = 255;
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
    public static byte[] Generate(int seed)
    {
        var canvas = new PixelCanvas(Protocol.AvatarSize, Protocol.AvatarSize);
        var random = new Random(seed);
        byte body = (byte)random.Next(4, 16), eyes = 1;
        canvas.Apply(new("fill", 0, 0, 0, 0, (byte)random.Next(2, 16)));
        for (int y = 8; y < 59; y++)
            for (int x = 7; x < 57; x++)
                if ((x - 32) * (x - 32) + (y - 33) * (y - 33) < 24 * 24)
                    canvas.Pixels[y * 64 + x] = body;
        int eyeY = random.Next(24, 31);
        canvas.Apply(new("pencil", 22, eyeY, 22, eyeY + 2, eyes, 4));
        canvas.Apply(new("pencil", 42, eyeY, 42, eyeY + 2, eyes, 4));
        int expression = random.Next(3);
        canvas.Apply(new("pencil", 23, 40, 32, expression == 0 ? 46 : 40, eyes, 2));
        canvas.Apply(new("pencil", 32, expression == 0 ? 46 : 40, 41, expression == 2 ? 44 : 40, eyes, 2));
        return canvas.Pixels;
    }
}
