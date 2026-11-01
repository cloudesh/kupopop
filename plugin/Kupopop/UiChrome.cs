using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Kupopop;

public static class UiChrome
{
    public static void CanvasTarget(string id, Vector2 size, bool writable)
    {
        // Read-only sheets must not claim mouse input before their overlaid
        // vote buttons. Only the artist/editor needs an active drawing target.
        if (writable) ImGui.InvisibleButton(id, size);
        else ImGui.Dummy(size);
    }

    // Return a move request for the next frame, so the paper and its contents
    // always move together rather than shifting halfway through drawing.
    public static Vector2 DragHandle(Vector2 size, bool locked, bool enabled = true)
    {
        ImGui.InvisibleButton("paper-drag", size);
        if (ImGui.IsItemHovered())
        {
            if (enabled && !locked) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                ImGui.SetTooltip(!enabled ? "Move this book with the standard title bar." : locked ? "Pinned. Unpin to move this book." : "Drag near the top of the page to move this book.");
        }
        return enabled && !locked && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 1)
            ? ImGui.GetIO().MouseDelta : Vector2.Zero;
    }

    public static Vector2 EmptyHeaderDrag(Vector2 min, Vector2 max, ref bool active, bool locked, bool enabled = true)
    {
        if (!enabled || max.X <= min.X || max.Y <= min.Y) { active = false; return Vector2.Zero; }
        // Submitted after header controls: their hover/active IDs take priority.
        // Keep submitting our own active handle when the pointer leaves the strip.
        bool anotherControlHovered = ImGui.GetCurrentContext().HoveredId != 0;
        bool heldByAnotherControl = ImGui.IsAnyItemActive() && !ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        if (!active && (anotherControlHovered || heldByAnotherControl || !ImGui.IsWindowHovered())) return Vector2.Zero;
        var cursor = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(min);
        ImGui.PushID("empty-header");
        var movement = DragHandle(max - min, locked);
        active = ImGui.IsItemActive();
        ImGui.PopID();
        ImGui.SetCursorScreenPos(cursor);
        return movement;
    }
}
