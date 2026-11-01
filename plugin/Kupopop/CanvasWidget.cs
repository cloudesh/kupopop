using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility.Raii;
using Kupopop.Shared;

namespace Kupopop;

public sealed class CanvasWidget : IDisposable
{
    private IDalamudTextureWrap? texture;
    private int revision = -1;
    private long textureAt;
    private bool dragging;
    private int x, y;
    private long sentAt;
    private Guid stroke;
    private int anchorX, anchorY;
    private BrushCursor? lastPointer;
    private long pointerAt;
    public string Tool = "pencil";
    public byte Color = 1;
    public int Size = 3;
    public Vector2 RenderSize { get; private set; }
    public Vector2 RenderOrigin { get; private set; }
    public BrushCursor? RemoteCursor;
    public Vector4? InputBlock;

    public void Toolbar(string id, bool transparency = false, Action? undo = null, bool canUndo = false)
    {
        ImGui.PushID(id);
        try
        {
            float available = ImGui.GetContentRegionAvail().X;
            float brushWidth = Math.Min(UiTheme.S(90), Math.Max(UiTheme.S(50), available - UiTheme.S(270)));
            foreach (string tool in new[] { "pencil", "fill", "eraser" })
            {
                if (tool != "pencil") ImGui.SameLine();
                using var background = ImRaii.PushColor(ImGuiCol.Button, Tool == tool ? UiTheme.RoseTint : UiTheme.Inset);
                using var text = ImRaii.PushColor(ImGuiCol.Text, Tool == tool ? UiTheme.Rose : UiTheme.Ink);
                var icon = tool == "pencil" ? FontAwesomeIcon.PencilAlt : tool == "fill" ? FontAwesomeIcon.FillDrip : FontAwesomeIcon.Eraser;
                using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
                    if (UiTheme.GlyphButton(tool, icon.ToIconString(), UiTheme.V(28, 28))) Tool = tool;
                if (ImGui.IsItemHovered() || ImGui.IsItemFocused())
                    UiTheme.Tooltip(tool == "pencil" ? "Pencil" : tool == "fill" ? "Fill bucket" : transparency ? "Erase to transparent" : "Eraser");
            }
            ImGui.SameLine();
            using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            using (ImRaii.PushColor(ImGuiCol.Button, DrawingTools.Shape(Tool) ? UiTheme.RoseTint : UiTheme.Inset))
                if (UiTheme.GlyphButton("shapes", FontAwesomeIcon.Circle.ToIconString(), UiTheme.V(28, 28))) ImGui.OpenPopup("shape-menu");
            if (ImGui.IsItemHovered()) UiTheme.Tooltip("Shapes");
            if (ImGui.BeginPopup("shape-menu"))
            {
                foreach (string shape in new[] { "line", "rectangle", "ellipse" })
                    if (ImGui.Selectable(char.ToUpperInvariant(shape[0]) + shape[1..], Tool == shape)) Tool = shape;
                ImGui.EndPopup();
            }
            if (available >= UiTheme.S(300)) ImGui.SameLine();
            ImGui.SetNextItemWidth(brushWidth);
            ImGui.SliderInt("##brush-size", ref Size, 1, 20, "%d px");
            if (ImGui.IsItemHovered() || ImGui.IsItemFocused()) UiTheme.Tooltip("Brush size");
            ImGui.SameLine();
            using (ImRaii.Disabled(!canUndo || undo is null))
            using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
                if (UiTheme.GlyphButton("undo", FontAwesomeIcon.Undo.ToIconString(), UiTheme.V(28, 28))) { dragging = false; undo?.Invoke(); }
            if (ImGui.IsItemHovered() || ImGui.IsItemFocused()) UiTheme.Tooltip("Undo last stroke");
            ImGui.SameLine(); UiPalette.Button(ref Color, transparency);
        }
        finally { ImGui.PopID(); }
    }


    public void Draw(string id, PixelCanvas canvas, Vector2 available, bool writable, Action<DrawOperation> submit, Action<BrushCursor>? pointer = null)
    {
        long now = Environment.TickCount64;
        if (texture is null || (revision != canvas.Revision && now - textureAt >= 40))
        {
            var next = Plugin.TextureProvider.CreateFromRaw(RawImageSpecification.Rgba32(canvas.Width, canvas.Height), canvas.Rgba(), "Kupopop canvas");
            texture?.Dispose(); texture = next;
            revision = canvas.Revision;
            textureAt = now;
        }
        float ratio = (float)canvas.Width / canvas.Height;
        // Reserve only the few pixels occupied by the two sheets underneath.
        available = Vector2.Max(new Vector2(8), available - UiTheme.V(9, 10));
        float width = Math.Max(8, Math.Min(available.X, available.Y * ratio));
        var size = new Vector2(width, width / ratio);
        RenderSize = size;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (available.X - size.X) / 2));
        Vector2 top = ImGui.GetCursorScreenPos();
        RenderOrigin = top;
        var mousePosition = ImGui.GetMousePos();
        bool blocked = InputBlock is { } block && mousePosition.X >= block.X && mousePosition.Y >= block.Y && mousePosition.X <= block.Z && mousePosition.Y <= block.W;
        UiChrome.CanvasTarget(id, size, writable && !blocked);
        UiTheme.SheetEdges(top, top + size);
        if (canvas.Transparency)
        {
            var draw = ImGui.GetWindowDrawList();
            float cell = UiTheme.S(12);
            for (int row = 0; row * cell < size.Y; row++)
                for (int col = 0; col * cell < size.X; col++)
                    draw.AddRectFilled(top + new Vector2(col * cell, row * cell),
                        top + Vector2.Min(size, new Vector2((col + 1) * cell, (row + 1) * cell)),
                        UiTheme.C((row + col) % 2 == 0 ? UiTheme.Paper : UiTheme.Inset));
        }
        ImGui.GetWindowDrawList().AddImage(texture.Handle, top, top + size);
        ImGui.GetWindowDrawList().AddRect(top - UiTheme.V(1, 1), top + size + UiTheme.V(1, 1), UiTheme.C(UiTheme.Line));
        bool down = writable && !blocked && ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left);
        Vector2 mouse = ImGui.GetMousePos() - top;
        int nx = Math.Clamp((int)(mouse.X / size.X * canvas.Width), 0, canvas.Width - 1);
        int ny = Math.Clamp((int)(mouse.Y / size.Y * canvas.Height), 0, canvas.Height - 1);
        var cursor = new BrushCursor(nx, ny, Tool, Color, Size, writable && !blocked && (ImGui.IsItemHovered() || down));
        bool pointerChanged = cursor.Visible ? lastPointer != cursor : lastPointer?.Visible == true;
        if (pointer is not null && (pointerChanged && now - pointerAt >= 60 || now - pointerAt >= 1000 && cursor.Visible))
        { pointer(cursor); lastPointer = cursor; pointerAt = now; }
        if (down)
        {
            if (!dragging) { stroke = Guid.NewGuid(); anchorX = nx; anchorY = ny; }
            if (DrawingTools.Shape(Tool))
                Preview(DrawingTools.Constrain(new(Tool, anchorX, anchorY, nx, ny, Color, Size)), top, size, canvas);
            else if (!dragging || (Tool != "fill" && now - sentAt >= 33 && (nx != x || ny != y)))
            {
                submit(new(Tool, dragging ? x : nx, dragging ? y : ny, nx, ny, Color, Size, stroke));
                x = nx; y = ny; sentAt = now;
            }
        }
        else if (dragging && writable && !blocked && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            if (DrawingTools.Shape(Tool)) submit(new(Tool, anchorX, anchorY, nx, ny, Color, Size, stroke));
            else if (Tool != "fill" && (nx != x || ny != y)) submit(new(Tool, x, y, nx, ny, Color, Size, stroke));
        }
        if (RemoteCursor is { Visible: true } remote) DrawCursor(remote, top, size, canvas);
        dragging = down;
    }

    private static void Preview(DrawOperation op, Vector2 origin, Vector2 size, PixelCanvas canvas)
    {
        var scale = size / new Vector2(canvas.Width, canvas.Height);
        Vector2 a = origin + new Vector2(op.X1, op.Y1) * scale, b = origin + new Vector2(op.X2, op.Y2) * scale;
        var draw = ImGui.GetWindowDrawList();
        uint color = op.Color == Palette.Transparent ? UiTheme.C(UiTheme.CanvasAccent) : Palette.Colors[op.Color];
        float thickness = Math.Max(1, op.Size * scale.X);
        draw.PushClipRect(origin, origin + size, true);
        if (op.Tool == "line") draw.AddLine(a, b, color, thickness);
        else if (op.Tool is "rectangle" or "square") draw.AddRect(Vector2.Min(a, b), Vector2.Max(a, b), color, 0, 0, thickness);
        else
        {
            Vector2 center = (a + b) / 2, radius = Vector2.Abs(b - a) / 2;
            Vector2 previous = center + new Vector2(radius.X, 0);
            for (int i = 1; i <= 64; i++)
            {
                float angle = i * MathF.Tau / 64;
                Vector2 next = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                draw.AddLine(previous, next, color, thickness); previous = next;
            }
        }
        draw.PopClipRect();
    }

    private static void DrawCursor(BrushCursor cursor, Vector2 origin, Vector2 size, PixelCanvas canvas)
    {
        var draw = ImGui.GetWindowDrawList();
        Vector2 point = origin + new Vector2(cursor.X + 0.5f, cursor.Y + 0.5f) * size / new Vector2(canvas.Width, canvas.Height);
        float radius = Math.Max(UiTheme.S(4), cursor.Size * size.X / canvas.Width / 2);
        draw.PushClipRect(origin, origin + size, true);
        draw.AddCircle(point, radius + 1, UiTheme.C(UiTheme.CanvasInk), 24, UiTheme.S(2));
        draw.AddCircle(point, radius, Palette.Colors[cursor.Color], 24, UiTheme.S(1.5f));
        var icon = cursor.Tool switch { "fill" => FontAwesomeIcon.FillDrip, "eraser" => FontAwesomeIcon.Eraser,
            "circle" or "ellipse" => FontAwesomeIcon.Circle, "square" or "rectangle" => FontAwesomeIcon.Square,
            "line" => FontAwesomeIcon.Slash, _ => FontAwesomeIcon.PencilAlt };
        using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            draw.AddText(point + UiTheme.V(10, 6), UiTheme.C(UiTheme.CanvasAccent), icon.ToIconString());
        draw.PopClipRect();
    }

    public void Dispose() { texture?.Dispose(); texture = null; }
}
