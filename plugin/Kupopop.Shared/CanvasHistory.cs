namespace Kupopop.Shared;

/// <summary>Bounded snapshots, grouped by a pointer stroke rather than network segments.</summary>
public sealed class CanvasHistory(int capacity = 24)
{
    private readonly List<byte[]> history = [];
    private Guid lastStroke;
    public bool CanUndo => history.Count > 0;
    public void Capture(PixelCanvas canvas, Guid stroke)
    {
        if (stroke != Guid.Empty && stroke == lastStroke) return;
        if (history.Count >= capacity) history.RemoveAt(0);
        history.Add((byte[])canvas.Pixels.Clone());
        lastStroke = stroke;
    }
    public bool Undo(PixelCanvas canvas)
    {
        if (history.Count == 0) return false;
        canvas.Load(history[^1]); history.RemoveAt(history.Count - 1); lastStroke = Guid.Empty;
        return true;
    }
    public void Clear() { history.Clear(); lastStroke = Guid.Empty; }
}
