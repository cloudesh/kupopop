using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Kupopop;

public sealed partial class MainWindow
{
    private readonly PomMotion coverPom = new();
    private long coverTwinkleUntil;
    private int coverTaps;

    private void DrawCoverMoogle(Vector2 center)
    {
        var saved = ImGui.GetCursorScreenPos();
        bool moving = !plugin.Config.ReduceMotion;
        coverPom.Step(0, ImGui.GetIO().DeltaTime, moving);
        ImGui.SetCursorScreenPos(center - UiTheme.V(34, 44));
        bool tapped = ImGui.InvisibleButton("tap-cover-moogle", UiTheme.V(70, 68));
        bool hovered = ImGui.IsItemHovered();
        if (tapped)
        {
            coverTaps++;
            coverTwinkleUntil = Environment.TickCount64 + 850;
            if (moving) coverPom.Nudge(coverTaps % 2 == 0 ? -3.5f : 3.5f);
        }
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            UiTheme.Tooltip("Give the little artist a tap, kupo.");
        }
        UiStationery.Logo(center, UiTheme.S(23), coverPom.Angle);
        float remaining = Math.Clamp((coverTwinkleUntil - Environment.TickCount64) / 850f, 0, 1);
        if (remaining > 0 || hovered)
        {
            float progress = moving ? 1 - remaining : 0;
            float opacity = remaining > 0 ? Math.Min(1, remaining * 3) : 0.3f;
            var draw = ImGui.GetWindowDrawList();
            for (int i = 0; i < 4; i++)
            {
                float angle = -2.8f + i * 0.9f;
                var point = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * UiTheme.S(30 + progress * 9);
                var color = i % 2 == 0 ? UiTheme.Rose : UiTheme.Brass; color.W = opacity;
                float radius = UiTheme.S(2.6f);
                draw.AddLine(point - new Vector2(radius, 0), point + new Vector2(radius, 0), UiTheme.C(color), UiTheme.S(1.3f));
                draw.AddLine(point - new Vector2(0, radius), point + new Vector2(0, radius), UiTheme.C(color), UiTheme.S(1.3f));
            }
        }
        ImGui.SetCursorScreenPos(saved);
    }
}
