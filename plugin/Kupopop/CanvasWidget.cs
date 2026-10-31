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
    public string Tool = "pencil";
    public byte Color = 1;
    public int Size = 3;
    public Vector2 RenderSize { get; private set; }

    public void Toolbar(string id, bool transparency = false, Action? undo = null, bool canUndo = false)
    {
        ImGui.PushID(id);
        try
        {
            float available = ImGui.GetContentRegionAvail().X;
            float brushWidth = Math.Min(UiTheme.S(110), Math.Max(UiTheme.S(60), available - UiTheme.S(164)));
            foreach (string tool in new[] { "pencil", "fill", "eraser" })
            {
                if (tool != "pencil") ImGui.SameLine();
                using var background = ImRaii.PushColor(ImGuiCol.Button, Tool == tool ? UiTheme.RoseTint : UiTheme.Inset);
                using var text = ImRaii.PushColor(ImGuiCol.Text, Tool == tool ? UiTheme.Rose : UiTheme.Ink);
                var icon = tool == "pencil" ? FontAwesomeIcon.PencilAlt : tool == "fill" ? FontAwesomeIcon.FillDrip : FontAwesomeIcon.Eraser;
                using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
                    if (ImGui.Button(icon.ToIconString() + "##" + tool, UiTheme.V(28, 28))) Tool = tool;
                if (ImGui.IsItemHovered() || ImGui.IsItemFocused())
                    ImGui.SetTooltip(tool == "pencil" ? "Pencil" : tool == "fill" ? "Fill bucket" : transparency ? "Erase to transparent" : "Eraser");
            }
            ImGui.SameLine(); ImGui.SetNextItemWidth(brushWidth);
            ImGui.SliderInt("##brush-size", ref Size, 1, 20, "%d px");
            if (ImGui.IsItemHovered() || ImGui.IsItemFocused()) ImGui.SetTooltip("Brush size");
            ImGui.SameLine();
            using (ImRaii.Disabled(!canUndo || undo is null))
            using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
                if (ImGui.Button(FontAwesomeIcon.Undo.ToIconString() + "##undo", UiTheme.V(28, 28))) { dragging = false; undo?.Invoke(); }
            if (ImGui.IsItemHovered() || ImGui.IsItemFocused()) ImGui.SetTooltip("Undo last stroke");
            int colours = transparency ? Palette.Colors.Length : Palette.Transparent;
            float swatch = Math.Min(UiTheme.S(20), (available - UiTheme.S(2) * (colours - 1)) / colours);
            for (byte i = 0; i < colours; i++)
            {
                if (i != 0) ImGui.SameLine(0, UiTheme.S(2));
                ImGui.PushID(i);
                ImGui.InvisibleButton("colour", new Vector2(swatch, UiTheme.S(20)));
                var centre = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
                uint c = Palette.Colors[i];
                var colour = new Vector4((c & 255) / 255f, ((c >> 8) & 255) / 255f, ((c >> 16) & 255) / 255f, 1);
                if (i == Palette.Transparent) colour = UiTheme.Paper;
                var draw = ImGui.GetWindowDrawList();
                draw.AddCircleFilled(centre, swatch * 0.32f, UiTheme.C(colour));
                if (i == Palette.Transparent)
                    draw.AddLine(centre - new Vector2(swatch * 0.23f), centre + new Vector2(swatch * 0.23f), UiTheme.C(UiTheme.Rose), UiTheme.S(1.5f));
                draw.AddCircle(centre, swatch * 0.32f, UiTheme.C(UiTheme.Line));
                if (Color == i) draw.AddCircle(centre, swatch * 0.46f, UiTheme.C(UiTheme.Ink), 0, UiTheme.S(1.5f));
                if (ImGui.IsItemClicked()) Color = i;
                if (ImGui.IsItemHovered() || ImGui.IsItemFocused()) ImGui.SetTooltip(ColourNames[i]);
                ImGui.PopID();
            }
        }
        finally { ImGui.PopID(); }
    }

    private static readonly string[] ColourNames = ["White", "Ink", "Graphite", "Silver", "Rose", "Scarlet", "Amber", "Sunshine", "Moss", "Forest", "Aqua", "Sky", "Violet", "Plum", "Clay", "Peach", "Transparent"];

    public void Draw(string id, PixelCanvas canvas, Vector2 available, bool writable, Action<DrawOperation> submit)
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
        ImGui.InvisibleButton(id, size);
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
        bool down = writable && ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left);
        if (down)
        {
            Vector2 mouse = ImGui.GetMousePos() - top;
            int nx = Math.Clamp((int)(mouse.X / size.X * canvas.Width), 0, canvas.Width - 1);
            int ny = Math.Clamp((int)(mouse.Y / size.Y * canvas.Height), 0, canvas.Height - 1);
            if (!dragging || (Tool != "fill" && now - sentAt >= 33 && (nx != x || ny != y)))
            {
                if (!dragging) stroke = Guid.NewGuid();
                submit(new(Tool, dragging ? x : nx, dragging ? y : ny, nx, ny, Color, Size, stroke));
                x = nx; y = ny; sentAt = now;
            }
        }
        dragging = down;
    }

    public void Dispose() { texture?.Dispose(); texture = null; }
}
