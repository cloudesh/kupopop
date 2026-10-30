using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace Kupopop;

public static class UiRoomSettings
{
    public static void Draw(ref int seconds, ref int rounds, ref int hints, ref int capacity)
    {
        if (!ImGui.BeginTable("room-options-aligned", 2, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX)) return;
        try
        {
            // Labels, inputs, and help have separate shared rows, so a wrapped
            // label can never shift one input below its neighbor.
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0); UiTheme.Caption("Seconds per drawer");
            ImGui.TableSetColumnIndex(1); UiTheme.Caption("Rounds");
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0); Number("duration", ref seconds, 15, 300);
            ImGui.TableSetColumnIndex(1); Number("rounds", ref rounds, 1, 5);
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0); UiTheme.Caption("15 to 300 seconds");
            ImGui.TableSetColumnIndex(1); UiTheme.Caption("Everyone draws each round");
            ImGui.TableNextRow(0, UiTheme.S(10));
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0); UiTheme.Caption("Hint interval");
            ImGui.TableSetColumnIndex(1); UiTheme.Caption("Maximum artists");
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0); Number("hints", ref hints, 0, 300);
            ImGui.TableSetColumnIndex(1); Number("capacity", ref capacity, 2, 16);
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0); UiTheme.Caption("Seconds; 0 hides the letters");
            ImGui.TableSetColumnIndex(1); UiTheme.Caption("2 to 16, including you");
        }
        finally { ImGui.EndTable(); }
    }

    private static void Number(string id, ref int value, int minimum, int maximum)
    {
        using var unique = ImRaii.PushId(id);
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(UiTheme.S(6), Math.Max(0, (UiTheme.S(28) - ImGui.GetFontSize()) / 2)));
        float width = ImGui.GetContentRegionAvail().X;
        if (UiTheme.Button("-", UiTheme.S(26), quiet: true)) value = Math.Clamp(Math.Clamp(value, minimum, maximum) - 1, minimum, maximum);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Decrease " + id);
        ImGui.SameLine(0, UiTheme.S(3));
        ImGui.SetNextItemWidth(Math.Max(UiTheme.S(30), width - UiTheme.S(58)));
        ImGui.InputInt("##value", ref value, 0, 0);
        if (ImGui.IsItemDeactivatedAfterEdit()) value = Math.Clamp(value, minimum, maximum);
        ImGui.SameLine(0, UiTheme.S(3));
        if (UiTheme.Button("+", UiTheme.S(26), quiet: true)) value = Math.Clamp(Math.Clamp(value, minimum, maximum) + 1, minimum, maximum);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Increase " + id);
    }
}
