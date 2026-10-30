using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Kupopop;

public static class UiChrome
{
    // Return a move request for the next frame, so the paper and its contents
    // always move together rather than shifting halfway through drawing.
    public static Vector2 DragHandle(Vector2 size, bool locked, bool enabled = true)
    {
        ImGui.InvisibleButton("paper-drag", size);
        if (ImGui.IsItemHovered())
        {
            if (enabled && !locked) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                ImGui.SetTooltip(!enabled ? "Move this book with the standard title bar." : locked ? "Pinned. Unpin to move this book." : "Drag the moogle or title to move this book.");
        }
        return enabled && !locked && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 1)
            ? ImGui.GetIO().MouseDelta : Vector2.Zero;
    }
}
