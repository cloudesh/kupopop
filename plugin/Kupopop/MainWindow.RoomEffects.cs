using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kupopop.Shared;

namespace Kupopop;

public sealed partial class MainWindow
{
    private readonly long[] pomLandedAt = new long[Protocol.MaximumPlayers];

    private void EffectSettings()
    {
        bool enabled = selectedEffects != RoomEffect.None;
        if (ImGui.Checkbox("Room effects", ref enabled))
        {
            selectedEffects = enabled ? RoomEffect.All : RoomEffect.None;
        }
        UiTheme.Wrapped("Each turn has a 20% chance of one enabled drawing effect. Encore is a separate reward when everyone guesses.", UiTheme.Muted);
        if (!enabled)
        {
            return;
        }
        EffectToggle(RoomEffect.MirrorInk, "Draw on both halves of the page at once.");
        EffectToggle(RoomEffect.BorrowedCrayons, "Draw with three surprise colours and your eraser.");
        EffectToggle(RoomEffect.DoublePoints, "Double match points for guesses and drawing. Lifetime points stay at their usual rate.");
        EffectToggle(RoomEffect.StickerStarter, "Work a little starter doodle into the picture.");
        EffectToggle(RoomEffect.Encore, "When everyone guesses, take five seconds to toss poms onto the finished picture.");
    }

    private void EffectToggle(RoomEffect effect, string explanation)
    {
        bool included = selectedEffects.HasFlag(effect);
        if (ImGui.Checkbox(RoomEffects.Name(effect), ref included))
        {
            selectedEffects = included ? selectedEffects | effect : selectedEffects & ~effect;
        }
        if (ImGui.IsItemHovered())
        {
            UiTheme.Tooltip(explanation);
        }
    }

    private void DrawEffectStamp(string label)
    {
        var at = painting.RenderOrigin + UiTheme.V(6, 6);
        var pad = UiTheme.V(6, 3);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(at, at + pad * 2 + ImGui.CalcTextSize(label), UiTheme.C(UiTheme.StickyRose), UiTheme.S(3));
        draw.AddText(at + pad, UiTheme.C(UiTheme.Ink), label);
    }

    private void DrawEncore(RoomView state)
    {
        DrawEffectStamp("Encore! Everyone guessed");
        var draw = ImGui.GetWindowDrawList();
        for (int i = 1; i <= state.PomCount; i++)
        {
            // Stable landing spots let a reconnect reconstruct the decorations from a count.
            float angle = i * 2.4f;
            float radius = 0.15f + 0.2f * i / Protocol.MaximumPlayers;
            var point = painting.RenderOrigin + painting.RenderSize *
                (new Vector2(0.5f) + radius * new Vector2(MathF.Cos(angle), MathF.Sin(angle)));
            float t = Math.Clamp((Environment.TickCount64 - pomLandedAt[i]) / 320f, 0, 1);
            if (!plugin.Config.ReduceMotion && pomLandedAt[i] != 0)
            {
                point.Y -= UiTheme.S(26) * (1 - t);
            }
            draw.AddCircleFilled(point, UiTheme.S(7), UiTheme.C(UiTheme.CanvasInk));
            draw.AddCircleFilled(point, UiTheme.S(6), UiTheme.C(UiTheme.CanvasAccent));
            draw.AddCircleFilled(point - UiTheme.V(2, 2), UiTheme.S(1.5f), 0xffffffff);
        }
        if (state.CanTossPom)
        {
            var saved = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(painting.RenderOrigin + new Vector2((painting.RenderSize.X - UiTheme.S(110)) / 2,
                painting.RenderSize.Y - UiTheme.S(36)));
            if (UiTheme.Button("Toss a pom", UiTheme.S(110), primary: true))
            {
                Send(new("encore-pom", TurnId: state.TurnId));
            }
            ImGui.SetCursorScreenPos(saved);
        }
    }
}
