using System.Numerics;
using Dalamud.Bindings.ImGui;
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
    public string Tool = "pencil";
    public byte Color = 1;
    public int Size = 3;

    public void Toolbar(string id)
    {
        ImGui.PushID(id);
        try
        {
            float available = ImGui.GetContentRegionAvail().X;
            float brushWidth = UiTheme.S(75);
            float buttonWidth = Math.Min(UiTheme.S(88), Math.Max(UiTheme.S(55), (available - brushWidth - UiTheme.S(30)) / 3));
            foreach (string tool in new[] { "pencil", "fill", "eraser" })
            {
                if (tool != "pencil") ImGui.SameLine();
                using var background = ImRaii.PushColor(ImGuiCol.Button, Tool == tool ? UiTheme.RoseTint : UiTheme.Inset);
                using var text = ImRaii.PushColor(ImGuiCol.Text, Tool == tool ? UiTheme.Rose : UiTheme.Ink);
                string label = char.ToUpperInvariant(tool[0]) + tool[1..];
                if (ImGui.Button(label, new Vector2(buttonWidth, UiTheme.S(32)))) Tool = tool;
            }
            ImGui.SameLine(); ImGui.SetNextItemWidth(brushWidth);
            ImGui.SliderInt("##brush-size", ref Size, 1, 20, "%d px");
            if (ImGui.IsItemHovered() || ImGui.IsItemFocused()) ImGui.SetTooltip("Brush size");
            float swatch = Math.Min(UiTheme.S(22), (available - UiTheme.S(3) * 15) / 16);
            for (byte i = 0; i < Palette.Colors.Length; i++)
            {
                if (i != 0) ImGui.SameLine(0, UiTheme.S(3));
                ImGui.PushID(i);
                ImGui.InvisibleButton("colour", new Vector2(swatch, UiTheme.S(25)));
                var centre = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
                uint c = Palette.Colors[i];
                var colour = new Vector4((c & 255) / 255f, ((c >> 8) & 255) / 255f, ((c >> 16) & 255) / 255f, 1);
                var draw = ImGui.GetWindowDrawList();
                draw.AddCircleFilled(centre, swatch * 0.32f, UiTheme.C(colour));
                draw.AddCircle(centre, swatch * 0.32f, UiTheme.C(UiTheme.Line));
                if (Color == i) draw.AddCircle(centre, swatch * 0.46f, UiTheme.C(UiTheme.Ink), 0, UiTheme.S(1.5f));
                if (ImGui.IsItemClicked()) Color = i;
                if (ImGui.IsItemHovered() || ImGui.IsItemFocused()) ImGui.SetTooltip(ColourNames[i]);
                ImGui.PopID();
            }
        }
        finally { ImGui.PopID(); }
    }

    private static readonly string[] ColourNames = ["Paper", "Ink", "Graphite", "Silver", "Rose", "Scarlet", "Amber", "Sunshine", "Moss", "Forest", "Aqua", "Sky", "Violet", "Plum", "Clay", "Peach"];

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
        float width = Math.Max(40, Math.Min(available.X, Math.Max(40, available.Y) * ratio));
        var size = new Vector2(width, width / ratio);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (available.X - size.X) / 2));
        Vector2 top = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton(id, size);
        ImGui.GetWindowDrawList().AddRectFilled(top + UiTheme.V(2, 3), top + size + UiTheme.V(2, 3), UiTheme.C(UiTheme.Inset));
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
                submit(new(Tool, dragging ? x : nx, dragging ? y : ny, nx, ny, Color, Size));
                x = nx; y = ny; sentAt = now;
            }
        }
        dragging = down;
    }

    public void Dispose() { texture?.Dispose(); texture = null; }
}
