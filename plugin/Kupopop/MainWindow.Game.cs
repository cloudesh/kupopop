using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Kupopop.Shared;

namespace Kupopop;

public sealed partial class MainWindow
{
    private void Game()
    {
        var state = room!;
        using var roomCellPadding = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, UiTheme.V(4, 1));
        bool host = state.HostId == device?.AccountId;
        int online = state.Players.Count(p => p.Connected);
        var available = ImGui.GetContentRegionAvail();
        bool playing = state.Phase is "drawing" or "intermission";
        float startHeight = state.Phase is "waiting" or "results" ? UiTheme.S(34) : 0;
        float headerHeight = UiTheme.S(30);
        var layout = UiGameLayout.Fit(available - new Vector2(0, headerHeight + startHeight), UiTheme.Scale);
        if (playing)
        {
            for (int i = 0; i < 4; i++)
            {
                headerHeight = Math.Max(UiTheme.S(30), art.MeasureWord(state.Word, state.Hint, layout.Canvas).Y + UiTheme.S(4));
                layout = UiGameLayout.Fit(available - new Vector2(0, headerHeight + startHeight), UiTheme.Scale);
            }
        }
        var top = ImGui.GetCursorScreenPos();
        string round = state.Round > 0 ? $"Round {state.Round}/{state.Options.Rounds}" : "";
        ImGui.AlignTextToFramePadding();
        UiTheme.Ellipsis(state.Options.Title, layout.Artists + layout.Gap - ImGui.CalcTextSize(round).X - UiTheme.S(10));
        if (round.Length > 0)
        {
            ImGui.SameLine(0, UiTheme.S(6));
            UiTheme.Caption(round);
        }
        if (playing)
        {
            ImGui.SetCursorScreenPos(top + new Vector2(layout.Artists + layout.Gap, 0));
            DrawTurnHeader(state, layout.Canvas);
        }
        float right = top.X + available.X;
        float actionWidth = ImGui.CalcTextSize(state.Code + "Copy codeLeave").X + UiTheme.S(44);
        ImGui.SetCursorScreenPos(new Vector2(right - actionWidth, top.Y));
        ImGui.AlignTextToFramePadding();
        UiTheme.Caption(state.Code);
        ImGui.SameLine(0, UiTheme.S(3));
        if (UiTheme.Button("Copy code", 0, quiet: true))
        {
            ImGui.SetClipboardText(state.Code);
        }

        ImGui.SameLine(0, UiTheme.S(2));
        if (UiTheme.Button("Leave", 0, quiet: true))
        {
            Send(new("leave"));
            return;
        }
        ImGui.SetCursorScreenPos(top + new Vector2(0, headerHeight));
        if (startHeight > 0)
        {
            if (host)
            {
                using (ImRaii.Disabled(online < 2 || client?.Connected != true))
                {
                    if (UiTheme.Button(state.Phase == "results" ? "Play again" : "Start drawing", 0, primary: true))
                    {
                        Send(new("start"));
                    }
                }

                ImGui.SameLine();
                if (UiTheme.Button("Room settings", 0, quiet: true))
                {
                    EditRoomSettings();
                    return;
                }
                ImGui.SameLine();
                if (UiTheme.Button("Invite friends", 0, quiet: true))
                {
                    OpenFriends(inviting: true);
                    return;
                }
                if (online < 2)
                {
                    ImGui.SameLine();
                    ImGui.AlignTextToFramePadding();
                    UiTheme.Caption("Invite another artist to begin.");
                }
            }
            else
            {
                UiTheme.Caption("Waiting for the host to start.");
            }
        }
        var origin = top + new Vector2(layout.Inset, headerHeight + startHeight);
        ImGui.SetCursorScreenPos(origin);
        DrawPlayers(state, new Vector2(layout.Artists, layout.Canvas));
        ImGui.SetCursorScreenPos(origin + new Vector2(layout.Artists + layout.Gap, 0));
        DrawBoard(state, new Vector2(layout.Canvas + UiTheme.S(9), layout.Canvas + UiTheme.S(10)));
        ImGui.SetCursorScreenPos(origin + new Vector2(layout.Artists + layout.Gap * 2 + layout.Canvas + UiTheme.S(9), 0));
        ChatScreen(state, new Vector2(layout.Chat, layout.Canvas));
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(layout.Width, layout.Canvas + UiTheme.S(10)));
    }

    private void DrawTurnHeader(RoomView state, float width)
    {
        var measured = art.MeasureWord(state.Word, state.Hint, width);
        float inset = Math.Max(0, (width - measured.X) / 2);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + inset);
        if (state.Word is { } word)
        {
            art.WordHeading(word, width - inset);
        }
        else
        {
            art.Hint(state.Hint, width);
        }

        if (state.Word is not null && ImGui.IsItemHovered())
        {
            UiTheme.Tooltip(state.Phase == "intermission" ? "The word was" : "Your word to draw");
        }
    }

    private void DrawPlayers(RoomView state, Vector2 size)
    {
        using var panel = new UiTheme.PaperPanel("artists", size, compact: true, note: UiTheme.StickyYellow, flush: true);
        if (!panel.Success)
        {
            return;
        }

        UiTheme.Caption($"ARTISTS / {state.Players.Length}");
        foreach (var player in state.Players.OrderByDescending(p => p.Score))
        {
            var texture = avatars.Get(player.Profile);
            using var id = ImRaii.PushId(player.Profile.Id.ToString());
            bool drawing = player.IsDrawer && state.Phase is "drawing" or "choosing";
            if (drawing)
            {
                var row = ImGui.GetCursorScreenPos();
                var draw = ImGui.GetWindowDrawList();
                draw.AddRectFilled(row - UiTheme.V(3, 2), row + new Vector2(ImGui.GetContentRegionAvail().X, UiTheme.S(42)), UiTheme.C(UiPresentation.Alpha(UiTheme.Rose, 0.12f)), UiTheme.S(3));
                draw.AddRectFilled(row - UiTheme.V(3, 2), row + UiTheme.V(0, 42), UiTheme.C(UiTheme.Rose), UiTheme.S(2));
            }
            ImGui.BeginGroup();
            UiPortrait.Draw(texture.Handle, UiTheme.V(40, 40));
            if (drawing && ImGui.IsItemHovered())
            {
                UiTheme.Tooltip("Current drawer");
            }

            ImGui.SameLine();
            ImGui.BeginGroup();
            UiTheme.Ellipsis(player.Profile.DisplayName, Math.Max(UiTheme.S(35), ImGui.GetContentRegionAvail().X));
            ImGui.TextColored(player.Solved ? UiTheme.Green : UiTheme.Muted, $"{player.Score:N0} pts");
            ImGui.EndGroup();
            ImGui.EndGroup();
            if (player.Profile.Id != device?.AccountId && ImGui.BeginPopupContextItem("artist-actions"))
            {
                SocialActions(player.Profile.Id);
                if (state.HostId == device?.AccountId && ImGui.MenuItem("Ban from room"))
                {
                    Send(new("kick", Text: player.Profile.Id.ToString()));
                }

                ImGui.EndPopup();
            }
            UiTheme.Rule();
        }
    }

    private void DrawBoard(RoomView state, Vector2 size)
    {
        bool canDraw = client?.Connected == true && state.Phase == "drawing" && state.DrawerId == device?.AccountId;
        var tools = CanvasToolsRect(ImGui.GetCursorScreenPos(), size - UiTheme.V(9, 10));
        painting.InputBlock = canDraw ? new Vector4(tools.Min.X, tools.Min.Y, tools.Max.X, tools.Max.Y) : null;
        painting.MirrorInk = state.Phase == "drawing" && state.Effect == RoomEffect.MirrorInk;
        painting.Crayons = state.Phase == "drawing" ? state.Crayons : null;
        painting.Draw("game-canvas", canvas, size, canDraw && !undoPending, operation =>
        {
            if (Send(new("draw", Draw: operation, TurnId: state.TurnId)))
            {
                canvas.Apply(operation);
                if (painting.MirrorInk)
                {
                    canvas.Apply(RoomEffects.Reflect(operation));
                }
                draws++;
                canUndoDrawing = true;
            }
        }, canDraw ? pointer => Send(new("cursor", TurnId: state.TurnId, Cursor: pointer)) : null);
        if (state.Phase == "choosing" && state.DrawerId == device?.AccountId)
        {
            DrawWordChoices(state);
        }

        bool encore = state.EncoreUntil > DateTimeOffset.UtcNow + serverOffset;
        if (!encore && state.Phase is ("choosing" or "drawing" or "intermission"))
        {
            DrawCanvasTimer(state);
        }
        if (state.Phase == "drawing" && state.Effect != RoomEffect.None)
        {
            DrawEffectStamp(RoomEffects.Name(state.Effect));
        }

        if (canDraw)
        {
            DrawCanvasTools(state);
        }

        if (state.Phase == "drawing" && state.DrawerId != device?.AccountId)
        {
            DrawCanvasVotes(state);
        }

        if (state.Phase == "intermission")
        {
            if (encore)
            {
                DrawEncore(state);
            }
            else
            {
                DrawTurnReveal(state);
            }
        }

        if (state.Phase == "results" && resultsDismissed != state.TurnId)
        {
            DrawMatchResults(state);
        }

        var rendered = new Vector2(MathF.Round(painting.RenderSize.X), MathF.Round(painting.RenderSize.Y));
        if (rendered != lastCanvasSize && Environment.TickCount64 >= nextLayoutLog)
        {
            log.Write($"ui.canvas size={rendered.X}x{rendered.Y} scale={UiTheme.Scale:F2}");
            lastCanvasSize = rendered;
            nextLayoutLog = Environment.TickCount64 + 1000;
        }
    }

    private void ChatScreen(RoomView state, Vector2 size)
    {
        using var panel = new UiTheme.PaperPanel("chat-panel", size, compact: true, note: UiTheme.StickyBlue, scrollable: false, plainTop: true, flush: true);
        if (!panel.Success)
        {
            return;
        }

        using var messageBackground = ImRaii.PushColor(ImGuiCol.ChildBg, Vector4.Zero);
        using var messageBorder = ImRaii.PushColor(ImGuiCol.Border, Vector4.Zero);
        using var messageBorderSize = ImRaii.PushStyle(ImGuiStyleVar.ChildBorderSize, 0);
        using var messagePadding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        using var scrollbar = ImRaii.PushColor(ImGuiCol.ScrollbarBg, Vector4.Zero);
        using (var messages = ImRaii.Child("messages", new Vector2(0, -UiTheme.S(34)), false))
        {
            if (messages.Success)
            {
                using var spacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, UiTheme.V(4, 2));
                foreach (var line in chat)
                {
                    DrawChatCard(line);
                }
                if (scrollChat)
                {
                    ImGui.SetScrollHereY(1);
                    scrollChat = false;
                }
            }
        }
        using (ImRaii.Disabled(client?.Connected != true))
        {
            using var inputColor = ImRaii.PushColor(ImGuiCol.FrameBg, Vector4.Lerp(UiTheme.StickyBlue, UiTheme.Panel, UiTheme.DarkMode ? 0.50f : 0.60f));
            using var inputHover = ImRaii.PushColor(ImGuiCol.FrameBgHovered, Vector4.Lerp(UiTheme.StickyBlue, UiTheme.Panel, 0.75f));
            using var inputActive = ImRaii.PushColor(ImGuiCol.FrameBgActive, Vector4.Lerp(UiTheme.StickyBlue, UiTheme.Panel, 0.85f));
            ImGui.SetNextItemWidth(Math.Max(UiTheme.S(60), ImGui.GetContentRegionAvail().X - UiTheme.S(48)));
            if (focusChat)
            {
                ImGui.SetKeyboardFocusHere();
                focusChat = false;
            }
            bool enter = ImGui.InputTextWithHint("##message", state.YouAreDone ? "Say something..." : "Talk or guess...", ref chatInput, 300, ImGuiInputTextFlags.EnterReturnsTrue);
            if (ImGui.IsItemHovered())
            {
                UiTheme.Tooltip(state.YouAreDone ? "Done mode: only the drawer and correct guessers see your messages." : "Talk and guess in the same chat.");
            }

            if (enter)
            {
                ImGui.SetKeyboardFocusHere(-1);
            }

            ImGui.SameLine();
            bool click = ToolAction(FontAwesomeIcon.PaperPlane, "Send");
            if ((enter || click) && !string.IsNullOrWhiteSpace(chatInput))
            {
                Send(new("chat", Text: chatInput, TurnId: state.TurnId));
                chatInput = "";
                focusChat = true;
            }
        }
    }
}
