using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Kupopop;

public static class UiPortrait
{
    public static void Draw(ImTextureID texture, Vector2 size)
    {
        // Presentation only: never replace transparent pixels in the stored avatar.
        var origin = ImGui.GetCursorScreenPos();
        Backdrop(origin, size);
        ImGui.Image(texture, size);
    }

    public static void DrawAt(ImTextureID texture, Vector2 origin, Vector2 size)
    {
        Backdrop(origin, size);
        ImGui.GetWindowDrawList().AddImage(texture, origin, origin + size);
    }

    private static void Backdrop(Vector2 origin, Vector2 size)
    {
        if (UiTheme.DarkMode) ImGui.GetWindowDrawList().AddRectFilled(origin, origin + size, 0xFFFFFFFF);
    }
}
