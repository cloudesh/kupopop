using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Kupopop;

public sealed unsafe class UiAspectConstraint
{
    // Keep the delegate alive for every native Begin/resize callback.
    private readonly ImGuiSizeCallback callback;
    private Vector2 minimum, maximum;

    public UiAspectConstraint() { callback = Resize; }

    public void Apply(Vector2 display, float scale)
    {
        var bounds = UiWindowGeometry.Bounds(display / scale);
        minimum = bounds.Minimum * scale; maximum = bounds.Maximum * scale;
        ImGui.SetNextWindowSizeConstraints(minimum, maximum, callback);
    }

    private void Resize(ImGuiSizeCallbackData* data)
        => data->DesiredSize = UiWindowGeometry.Constrain(data->DesiredSize, data->CurrentSize, minimum, maximum);
}
