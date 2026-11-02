using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Kupopop.Shared;

namespace Kupopop;

public sealed partial class MainWindow
{
    private void Menu()
    {
        var available = ImGui.GetContentRegionAvail();
        float width = Math.Min(available.X, UiTheme.S(900));
        var cover = ImGui.GetCursorScreenPos() + new Vector2(Math.Max(0, (available.X - width) / 2), Math.Max(0, (available.Y - UiTheme.S(402)) / 2));
        DrawCoverMoogle(cover + UiTheme.V(35, 51));
        UiBrand.Wordmark(cover + UiTheme.V(80, 5), new Vector2(Math.Min(width - UiTheme.S(90), UiTheme.S(440)), UiTheme.S(74)), cover: true);
        var origin = cover + UiTheme.V(0, 92);
        ImGui.SetCursorScreenPos(origin);
        if (!ImGui.BeginTable("play-actions", 3, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX, new Vector2(width, 0)))
        {
            return;
        }

        try
        {
            ImGui.TableNextColumn();
            using (var card = new UiTheme.PaperPanel("join-card", new Vector2(0, UiTheme.S(250)), note: ActiveInvitations().Length > 0 ? UiTheme.InvitationPaper : UiTheme.StickyYellow, scrollable: false, noteAngle: -0.65f))
            {
                if (card.Success)
                {
                    var invitation = ActiveInvitations().FirstOrDefault();
                    if (invitation is not null)
                    {
                        UiTheme.InvitationGlow(ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize());
                    }

                    art.Heading(invitation is null ? "Join via code" : "You're invited!");
                    UiTheme.Wrapped(invitation is null ? "Enter a code from your friend below to play." : invitation.From.DisplayName + " invited you to " + invitation.RoomTitle, UiTheme.Muted);
                    UiTheme.Gap(10);
                    if (invitation is null)
                    {
                        UiTheme.Field("Room code", "##room-code", ref joinCode, 12, "Enter your room code");
                    }
                    else if (UiTheme.Button("Dismiss invitation", 0, quiet: true))
                    {
                        Send(new("dismiss-invite", TargetId: invitation.Id));
                    }

                    CardActionPosition();
                    using (ImRaii.Disabled(client?.Connected != true || invitation is null && string.IsNullOrWhiteSpace(joinCode)))
                    {
                        if (UiTheme.NoteButton(invitation is null ? "Join room" : "Join invited game", ImGui.GetContentRegionAvail().X, invitation is null ? UiTheme.StickyYellow : UiTheme.InvitationPaper, primary: true))
                        {
                            Send(invitation is null ? new("join", Text: joinCode) : new("accept-invite", TargetId: invitation.Id));
                        }
                    }
                }
            }
            ImGui.TableNextColumn();
            using (var card = new UiTheme.PaperPanel("make-card", new Vector2(0, UiTheme.S(250)), note: UiTheme.StickyRose, scrollable: false, noteAngle: 0.6f))
            {
                if (card.Success)
                {
                    art.Heading("Host Room");
                    UiTheme.Wrapped("Host your own room with default or custom rulesets", UiTheme.Muted);
                    UiTheme.Gap(10);
                    UiTheme.Caption("90 seconds  /  2 rounds");
                    UiTheme.Caption("Your room. Your rules.");
                    UiTheme.Gap(5);
                    CardActionPosition();
                    using (ImRaii.Disabled(client?.Connected != true))
                    {
                        if (UiTheme.NoteButton("Set up a room", ImGui.GetContentRegionAvail().X, UiTheme.StickyRose))
                        {
                            OpenRoomSetup();
                        }
                    }
                }
            }
            ImGui.TableNextColumn();
            using (var card = new UiTheme.PaperPanel("public-card", new Vector2(0, UiTheme.S(250)), note: UiTheme.StickySage, scrollable: false, noteAngle: -0.4f))
            {
                if (card.Success)
                {
                    art.Heading("Browse Lobbies");
                    UiTheme.Wrapped("Find publicly hosted rooms across all Data Centers", UiTheme.Muted);
                    UiTheme.Gap(10);
                    UiTheme.Caption("Browse open invitations");
                    UiTheme.Caption("Meet the other artists.");
                    UiTheme.Gap(5);
                    CardActionPosition();
                    using (ImRaii.Disabled(client?.Connected != true))
                    {
                        if (UiTheme.NoteButton("Find a room", ImGui.GetContentRegionAvail().X, UiTheme.StickySage))
                        {
                            screen = "public";
                            Send(new("list"));
                        }
                    }
                }
            }
        }
        finally { ImGui.EndTable(); }
        UiTheme.Gap(8);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + UiTheme.S(6), ImGui.GetCursorScreenPos().Y));
        if (profile is not null)
        {
            UiPortrait.Draw(avatars.Get(profile).Handle, UiTheme.V(42, 42));
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.TextUnformatted(profile.DisplayName);
            UiTheme.Caption("Your sketchbook, your signature.");
            ImGui.EndGroup();
        }
        else
        {
            UiTheme.Wrapped("Your profile will appear here when it has loaded.", UiTheme.Muted);
        }

        ImGui.SameLine();
        float suggestionWidth = ImGui.CalcTextSize("Suggestions").X + UiTheme.S(36);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + width - UiTheme.S(6) - suggestionWidth, ImGui.GetCursorScreenPos().Y));
        if (UiTheme.Button("Suggestions", suggestionWidth, primary: true))
        {
            ShowDebug = false;
            screen = "suggestions";
        }
    }

    private static void CardActionPosition()
    {
        float bottom = ImGui.GetCursorPosY() + ImGui.GetContentRegionAvail().Y - UiTheme.S(30);
        if (ImGui.GetCursorPosY() < bottom)
        {
            ImGui.SetCursorPosY(bottom);
        }
    }
}
