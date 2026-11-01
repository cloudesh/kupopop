using System.Numerics;

namespace Kupopop;

public sealed class UiDragAnchor
{
    private bool active;
    private Vector2 window, mouse;

    public void Begin(Vector2 windowPosition, Vector2 mousePosition)
    { active = true; window = windowPosition; mouse = mousePosition; }

    public Vector2? Position(Vector2 mousePosition, bool held, bool enabled)
    {
        if (!held || !enabled) active = false;
        return active ? window + mousePosition - mouse : null;
    }
}
