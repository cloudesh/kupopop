using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Kupopop.Shared;

namespace Kupopop;

public sealed partial class MainWindow
{
    private void PublicRooms()
    {
        if (PageHeading("Browse Lobbies", refresh: true))
        {
            return;
        }

        using var panel = new UiTheme.PaperPanel("public-list", Vector2.Zero);
        if (!panel.Success)
        {
            return;
        }

        if (publicRooms.Length == 0)
        {
            string invitation = "No rooms available, would you like to make your own?";
            float width = Math.Min(UiTheme.S(430), ImGui.GetContentRegionAvail().X);
            float textHeight = ImGui.CalcTextSize(invitation, false, width).Y;
            var origin = ImGui.GetCursorScreenPos() + new Vector2(Math.Max(0, (ImGui.GetContentRegionAvail().X - width) / 2), Math.Max(0, (ImGui.GetContentRegionAvail().Y - textHeight - UiTheme.S(52)) / 2));
            UiPresentation.DrawCenteredText(invitation, origin, new Vector2(width, textHeight));
            ImGui.SetCursorScreenPos(origin + new Vector2(Math.Max(0, (width - UiTheme.S(160)) / 2), textHeight + UiTheme.S(14)));
            if (UiTheme.Button("Host Room", UiTheme.S(160), primary: true))
            {
                OpenRoomSetup(publicByDefault: true);
            }

            return;
        }
        foreach (var listed in publicRooms)
        {
            using var id = ImRaii.PushId(listed.Code);
            if (!ImGui.BeginTable("room", 3))
            {
                continue;
            }

            try
            {
                ImGui.TableSetupColumn("Room", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Players", ImGuiTableColumnFlags.WidthFixed, UiTheme.S(110));
                ImGui.TableSetupColumn("Join", ImGuiTableColumnFlags.WidthFixed, UiTheme.S(100));
                ImGui.TableNextColumn();
                UiTheme.Ellipsis(listed.Title, ImGui.GetContentRegionAvail().X);
                UiTheme.Caption(listed.Phase is "drawing" or "intermission" or "choosing" ? "In progress · join and guess now" : "Ready for company");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{listed.Players} / {listed.MaxPlayers} artists");
                ImGui.TableNextColumn();
                using (ImRaii.Disabled(client?.Connected != true || listed.Players >= listed.MaxPlayers))
                {
                    if (UiTheme.Button("Join", UiTheme.S(80)))
                    {
                        Send(new("join", Text: listed.Code));
                    }
                }
            }
            finally { ImGui.EndTable(); }
            UiTheme.Rule();
        }
    }
}
