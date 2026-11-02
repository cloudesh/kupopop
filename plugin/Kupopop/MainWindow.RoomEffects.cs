using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kupopop.Shared;

namespace Kupopop;

public sealed partial class MainWindow
{
    private void EffectSettings()
    {
        bool enabled = selectedEffects != RoomEffect.None;
        if (ImGui.Checkbox("Room effects", ref enabled))
        {
            selectedEffects = enabled ? RoomEffect.All : RoomEffect.None;
        }
        UiTheme.Wrapped("Each turn has a 20% chance of one enabled drawing effect.", UiTheme.Muted);
        if (!enabled)
        {
            return;
        }
        EffectToggle(RoomEffect.MirrorInk, "Draw on both halves of the page at once.");
        EffectToggle(RoomEffect.BorrowedCrayons, "Draw with three surprise colours and your eraser.");
        EffectToggle(RoomEffect.DoublePoints, "Double match points for guesses and drawing. Lifetime points stay at their usual rate.");
        EffectToggle(RoomEffect.StickerStarter, "Work a little starter doodle into the picture.");
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

}
