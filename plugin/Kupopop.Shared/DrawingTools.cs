namespace Kupopop.Shared;

public static class DrawingTools
{
    public static bool Shape(string tool) => tool is "line" or "rectangle" or "square" or "ellipse" or "circle";
    public static bool Valid(string tool) => tool is "pencil" or "eraser" or "fill" || Shape(tool);
    public static DrawOperation Constrain(DrawOperation op)
    {
        if (op.Tool is not ("square" or "circle")) return op;
        int side = Math.Min(Math.Abs(op.X2 - op.X1), Math.Abs(op.Y2 - op.Y1));
        return op with { X2 = op.X1 + Math.Sign(op.X2 - op.X1) * side, Y2 = op.Y1 + Math.Sign(op.Y2 - op.Y1) * side };
    }
}
