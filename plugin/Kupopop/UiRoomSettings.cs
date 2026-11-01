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
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0); UiTheme.Caption("15 to 300 seconds");
            ImGui.TableSetColumnIndex(1); UiTheme.Caption("1 to 5 rounds");
            ImGui.TableSetColumnIndex(2); UiTheme.Caption("2 to 16, including you");
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
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0); UiTheme.Caption("Seconds; 0 hides the letters");
                ImGui.TableSetColumnIndex(1); UiTheme.Caption("1 to 8 private choices");
            }
            finally { ImGui.EndTable(); }
        }
        UiTheme.Gap(10);
        ImGui.Checkbox("Allow vote skip", ref voteSkip);
        ImGui.Checkbox("Allow vote kick", ref voteKick);
        UiTheme.Wrapped("Below five players, everyone except the drawer must agree. With five or more, 70% must agree.", UiTheme.Muted);
    }

    private static void Number(string id, ref int value, int minimum, int maximum)
    {
        using var unique = ImRaii.PushId(id);
        value = Math.Clamp(value, minimum, maximum);
        ImGui.SetNextItemWidth(-1);
        ImGui.SliderInt("##value", ref value, minimum, maximum, "", ImGuiSliderFlags.AlwaysClamp | ImGuiSliderFlags.NoInput);
        UiPresentation.CenteredLabel(value == 0 && id == "hints" ? "Off" : value.ToString(), ImGui.GetContentRegionAvail().X, UiTheme.Ink);
    }
}
