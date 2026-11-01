using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace Kupopop;

public static class UiRoomSettings
{
    public static void Basic(ref int seconds, ref int rounds, ref int capacity)
    {
        if (!ImGui.BeginTable("room-options-basic", 3, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX)) return;
        try
        {
            // Labels, inputs, and help have separate shared rows, so a wrapped
            // label can never shift one input below its neighbor.
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0); UiTheme.Caption("Seconds per drawer");
            ImGui.TableSetColumnIndex(1); UiTheme.Caption("Rounds");
            ImGui.TableSetColumnIndex(2); UiTheme.Caption("Maximum players");
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0); Number("duration", ref seconds, 15, 300);
            ImGui.TableSetColumnIndex(1); Number("rounds", ref rounds, 1, 5);
            ImGui.TableSetColumnIndex(2); Number("capacity", ref capacity, 2, 16);
        }
        finally { ImGui.EndTable(); }
    }

    public static void Advanced(ref int hints, ref int choices, ref bool voteKick, ref bool voteSkip)
    {
        if (ImGui.BeginTable("room-options-advanced", 2, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX))
        {
            try
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0); UiTheme.Caption("Hint interval");
                ImGui.TableSetColumnIndex(1); UiTheme.Caption("Words to choose from");
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0); Number("hints", ref hints, 0, 300);
                ImGui.TableSetColumnIndex(1); Number("word choices", ref choices, 1, 8);
            }
            finally { ImGui.EndTable(); }
        }
        UiTheme.Gap(10);
        ImGui.Checkbox("Allow vote skip", ref voteSkip);
        ImGui.Checkbox("Allow vote kick", ref voteKick);
        if (ImGui.IsItemHovered()) UiTheme.Tooltip("Below five players, everyone except the drawer must agree. With five or more, 70% must agree.");
    }

    private static void Number(string id, ref int value, int minimum, int maximum)
    {
        using var unique = ImRaii.PushId(id);
        value = Math.Clamp(value, minimum, maximum);
        float sliding = value;
        using var grab = ImRaii.PushStyle(ImGuiStyleVar.GrabMinSize, UiTheme.S(12));
        ImGui.SetNextItemWidth(-1);
        if (ImGui.SliderFloat("##value", ref sliding, minimum, maximum, "", ImGuiSliderFlags.AlwaysClamp | ImGuiSliderFlags.NoInput | ImGuiSliderFlags.NoRoundToFormat))
            value = Math.Clamp((int)MathF.Round(sliding), minimum, maximum);
        UiPresentation.CenteredLabel(value == 0 && id == "hints" ? "Off" : value.ToString(), ImGui.GetContentRegionAvail().X, UiTheme.Ink);
    }
}
