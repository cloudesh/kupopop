using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
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
        foreach (string tool in new[] { "pencil", "fill", "eraser" })
        {
            if (tool != "pencil") ImGui.SameLine();
            if (ImGui.RadioButton(tool, Tool == tool)) Tool = tool;
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        ImGui.SliderInt("Size", ref Size, 1, 20);
        for (byte i = 0; i < Palette.Colors.Length; i++)
        {
            if (i != 0) ImGui.SameLine(0, 3);
            ImGui.PushID(i);
            uint c = Palette.Colors[i];
            var color = new Vector4((c & 255) / 255f, ((c >> 8) & 255) / 255f, ((c >> 16) & 255) / 255f, 1);
            if (ImGui.ColorButton("color", color, ImGuiColorEditFlags.NoTooltip, new Vector2(20, 20))) Color = i;
            if (Color == i)
                ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), 0xFF3355FF, 0, ImDrawFlags.None, 2);
            ImGui.PopID();
        }
        ImGui.PopID();
    }

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
        Vector2 top = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton(id, size);
        ImGui.GetWindowDrawList().AddImage(texture.Handle, top, top + size);
        ImGui.GetWindowDrawList().AddRect(top, top + size, 0xFF777777);
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
