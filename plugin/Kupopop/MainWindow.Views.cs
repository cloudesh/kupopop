using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Kupopop.Shared;

namespace Kupopop;

public sealed partial class MainWindow
{
    private bool returningArtist, focusChat;
    private string lastNotice = "";
    private long noticeUntil;
    private Vector2 lastCanvasSize;
    private long nextLayoutLog;
    private readonly PomMotion pom = new();
    private Vector2? pendingWindowPosition;
    private static string UiVersion => typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "dev";

    public override void PreDraw()
    {
        var display = ImGui.GetIO().DisplaySize;
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        if (!plugin.Config.UseStandardFrame)
            Flags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoMove;
        AllowBackgroundBlur = plugin.Config.UseStandardFrame;
        AllowClickthrough = plugin.Config.UseStandardFrame;
        if (plugin.Config.PaperPinned is null) { plugin.Config.PaperPinned = IsPinned; plugin.Save(); }
        if (!plugin.Config.UseStandardFrame) IsPinned = plugin.Config.PaperPinned.Value;
        else if (plugin.Config.PaperPinned != IsPinned) { plugin.Config.PaperPinned = IsPinned; plugin.Save(); }
        Position = !IsPinned && !plugin.Config.UseStandardFrame ? pendingWindowPosition : null;
        PositionCondition = ImGuiCond.Always;
        pendingWindowPosition = null;
        // WindowSystem applies UI scale to these logical dimensions itself.
        var logicalDisplay = display / UiTheme.Scale;
        SizeConstraints = new()
        {
            MinimumSize = new(Math.Min(820, Math.Max(640, logicalDisplay.X - 40)), Math.Min(540, Math.Max(420, logicalDisplay.Y - 60))),
            MaximumSize = new(float.MaxValue),
        };
    }

    public override void Draw()
    {
        var position = ImGui.GetWindowPos();
        pom.Step(position.X + position.Y * 0.35f, ImGui.GetIO().DeltaTime, !plugin.Config.ReduceMotion);
        UiStationery.Book(position, position + ImGui.GetWindowSize(), plugin.Config.UseStandardFrame ? ImGui.GetFrameHeight() : 0);
        DrawHeader();
        UiTheme.Rule();
        DrawNotice();
        using (var transparent = ImRaii.PushColor(ImGuiCol.ChildBg, Vector4.Zero))
        using (var body = ImRaii.Child("page-content", new Vector2(0, Math.Max(UiTheme.S(100), ImGui.GetContentRegionAvail().Y - ImGui.GetTextLineHeightWithSpacing() - UiTheme.S(2))), false))
        {
            if (body.Success)
            {
                if (newRecovery.Length > 0) RecoveryScreen();
                else if (ShowDebug) ServerSettings();
                else if (screen == "welcome") Welcome();
                else if (screen == "profile") ProfileScreen();
                else if (screen == "game" && room is not null) Game();
                else if (screen == "public") PublicRooms();
                else if (screen == "create") CreateRoomScreen();
                else Menu();
            }
        }
        UiTheme.Caption(room is not null && screen == "game" ? "a page shared is a memory made" : "made for little masterpieces, kupo.");
        ImGui.SameLine(Math.Max(ImGui.GetCursorPosX() + UiTheme.S(20), ImGui.GetWindowContentRegionMax().X - UiTheme.S(100)));
        UiTheme.Caption("vol. " + UiVersion);
    }

    private void DrawHeader()
    {
        bool framed = plugin.Config.UseStandardFrame;
        if (!ImGui.BeginTable("brand-paper-controls", framed ? 3 : 4, ImGuiTableFlags.NoPadOuterX)) return;
        try
        {
            ImGui.TableSetupColumn("Brand", ImGuiTableColumnFlags.WidthFixed, UiTheme.S(188));
            ImGui.TableSetupColumn("Navigation", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Connection", ImGuiTableColumnFlags.WidthFixed, UiTheme.S(114));
            if (!framed) ImGui.TableSetupColumn("Paper controls", ImGuiTableColumnFlags.WidthFixed, UiTheme.S(64));
            ImGui.TableNextColumn();
            var face = ImGui.GetCursorScreenPos();
            var move = UiChrome.DragHandle(UiTheme.V(184, 45), IsPinned, !framed);
            if (move != Vector2.Zero) pendingWindowPosition = ImGui.GetWindowPos() + move;
            var afterBrand = ImGui.GetCursorScreenPos();
            UiStationery.Moogle(face + UiTheme.V(20, 30), UiTheme.S(14), pom.Angle);
            ImGui.SetCursorScreenPos(face + UiTheme.V(49, 0));
            ImGui.BeginGroup();
            art.Heading("Kupopop");
            UiTheme.Caption("THE LITTLE ART BOOK");
            ImGui.EndGroup();
            ImGui.SetCursorScreenPos(afterBrand);
            ImGui.TableNextColumn();
            UiTheme.Gap(6);
            if (newRecovery.Length == 0 && screen != "welcome") Navigation();
            else { ImGui.AlignTextToFramePadding(); UiTheme.Caption("THE DRAWING ROOM"); }
            ImGui.TableNextColumn();
            bool online = client?.Connected == true;
            UiTheme.Gap(6);
            ImGui.AlignTextToFramePadding();
            using (ImRaii.Disabled(busy))
            {
                if (device is not null && !online)
                { if (UiTheme.Button(busy ? "Connecting..." : "Connect", 0, primary: true)) Connect(); }
                else
                {
                    UiAssets.Glyph(FontAwesomeIcon.Circle, online ? UiTheme.Green : UiTheme.Muted);
                    ImGui.SameLine();
                    if (UiTheme.Button(online ? "Connected" : busy ? "Connecting..." : "Offline", 0, quiet: true)) ShowDebug = !ShowDebug;
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Connection settings");
                }
            }
            if (!framed)
            {
                ImGui.TableNextColumn(); UiTheme.Gap(6);
                if (ToolAction(FontAwesomeIcon.Thumbtack, IsPinned ? "Unpin book" : "Pin book", IsPinned))
                { IsPinned = !IsPinned; plugin.Config.PaperPinned = IsPinned; plugin.Save(); }
                ImGui.SameLine();
                if (ToolAction(FontAwesomeIcon.Times, "Close book")) IsOpen = false;
            }
        }
        finally { ImGui.EndTable(); }
    }

    private void Navigation()
    {
        if (UiTheme.Tab(room is null ? "Play" : "Your room", !ShowDebug && screen != "profile"))
        { ShowDebug = false; screen = room is null ? "menu" : "game"; }
        ImGui.SameLine();
        using (ImRaii.Disabled(profile is null))
            if (UiTheme.Tab("Profile", !ShowDebug && screen == "profile")) { ShowDebug = false; OpenProfile(); }
        ImGui.SameLine();
        if (UiTheme.Tab("Connection", ShowDebug)) ShowDebug = !ShowDebug;
    }

    private void DrawNotice()
    {
        if (string.IsNullOrWhiteSpace(status)) return;
        if (status != lastNotice) { lastNotice = status; noticeUntil = Environment.TickCount64 + 7000; }
        bool error = status.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("rejected", StringComparison.OrdinalIgnoreCase) || status.Contains("limit", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("need", StringComparison.OrdinalIgnoreCase) || status.Contains("full", StringComparison.OrdinalIgnoreCase);
        if (!error && Environment.TickCount64 > noticeUntil) return;
        if (newRecovery.Length > 0 && !error) return;
        UiTheme.Wrapped(status, error ? UiTheme.Rose : UiTheme.Green);
        UiTheme.Gap(3);
    }

    private void Welcome()
    {
        if (!ImGui.BeginTable("welcome-layout", 2)) return;
        try
        {
            ImGui.TableSetupColumn("Letter", ImGuiTableColumnFlags.WidthStretch, 1.05f);
            ImGui.TableSetupColumn("Account", ImGuiTableColumnFlags.WidthStretch, 1);
            ImGui.TableNextColumn();
            UiTheme.Gap(14);
            UiStationery.PageMark("a fresh sketchbook");
            UiTheme.Gap(12);
            art.Heading("A little imagination.");
            art.Heading("A lot of kupo.");
            UiTheme.Gap(16);
            UiTheme.Wrapped("An empty page, a few friends, and a word worth guessing. Welcome to your little drawing room in Eorzea.", UiTheme.Muted);
            UiTheme.Gap(24);
            art.Mascot(88);
            UiTheme.Gap(14);
            UiTheme.Caption("Draw a little. Guess together. Make it yours.");
            ImGui.TableNextColumn();
            using var panel = new UiTheme.PaperPanel("welcome-card", Vector2.Zero, taped: true);
            if (!panel.Success) return;
            UiTheme.Caption("FIRST VISIT");
            art.Heading("Make yourself at home");
            UiTheme.Gap(7);
            UiTheme.Wrapped("Choose a name for your Kupopop profile. You can draw your own avatar next.", UiTheme.Muted);
            UiTheme.Gap(9);
            using (ImRaii.Disabled(busy || client is null))
            {
                UiTheme.Field("Display name", "##new-name", ref displayName, 40, "Your character or artist name");
                using (ImRaii.Disabled(!Protocol.ValidName(displayName)))
                    if (UiTheme.Button(busy ? "Creating your profile..." : "Create my profile", ImGui.GetContentRegionAvail().X, primary: true)) CreateAccount(false);
                UiTheme.Gap(3);
                UiTheme.Wrapped("Your name is unverified. Players see your chosen name and avatar when you share a room.", UiTheme.Muted);
                UiTheme.Rule();
                if (UiTheme.Button(returningArtist ? "Hide account recovery" : "Already have a profile?", ImGui.GetContentRegionAvail().X, quiet: true)) returningArtist = !returningArtist;
                if (returningArtist)
                {
                    UiTheme.Field("Private recovery code", "##recover", ref recoveryInput, 128, "Paste your saved recovery code", ImGuiInputTextFlags.Password);
                    if (UiTheme.Button("Recover my profile", ImGui.GetContentRegionAvail().X)) CreateAccount(true);
                }
            }
            UiTheme.Gap(8);
            if (UiTheme.Button("Connection settings", 0, quiet: true)) ShowDebug = true;
        }
        finally { ImGui.EndTable(); }
    }

    private void RecoveryScreen()
    {
        using var panel = new UiTheme.PaperPanel("save-recovery", Vector2.Zero);
        if (!panel.Success) return;
        UiTheme.Caption("YOUR PROFILE IS READY");
        art.Heading("Keep your sketchbook safe");
        UiTheme.Gap(8);
        UiTheme.Wrapped("Save this private recovery code in your password manager. It is the way back to your account if you lose access to this device.", UiTheme.Muted);
        UiTheme.Gap(12);
        ImGui.Checkbox("Show my private code", ref revealRecovery);
        UiTheme.Field("Recovery code", "##new-recovery", ref newRecovery, 128, "", ImGuiInputTextFlags.ReadOnly |
            (revealRecovery ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password));
        if (UiTheme.Button("Copy recovery code")) ImGui.SetClipboardText(newRecovery);
        UiTheme.Wrapped("Anyone with this code can recover your profile. It is not saved in the plugin configuration or diagnostic logs.", UiTheme.Muted);
        UiTheme.Rule();
        ImGui.Checkbox("I have saved my code somewhere private", ref savedRecovery);
        if (profile is not null)
        {
            ImGui.Image(avatars.Get(profile).Handle, UiTheme.V(64, 64));
            ImGui.SameLine(); ImGui.TextUnformatted("A little face to get you started.");
        }
        using (ImRaii.Disabled(!savedRecovery))
        {
            if (UiTheme.Button("Use this avatar & continue", 0, primary: true)) { newRecovery = ""; screen = room is null ? "menu" : "game"; }
            ImGui.SameLine();
            if (UiTheme.Button("Draw my own")) { newRecovery = ""; OpenProfile(); }
        }
    }

    private void Menu()
    {
        UiStationery.PageMark("01 / the drawing room");
        art.Heading("Open a page. Make a little magic.");
        UiTheme.Wrapped("Join a friend, open your own room, or find some company.", UiTheme.Muted);
        UiTheme.Gap(6);
        if (!ImGui.BeginTable("play-actions", 3, ImGuiTableFlags.SizingStretchSame)) return;
        try
        {
            ImGui.TableNextColumn();
            using (var card = new UiTheme.PaperPanel("join-card", new Vector2(0, UiTheme.S(250)), note: UiTheme.StickyYellow))
            {
                if (card.Success)
                {
                    UiAssets.Glyph(FontAwesomeIcon.Key, UiTheme.Brass);
                    art.Heading("Join Room");
                    UiTheme.Wrapped("Have an invitation? There is a seat waiting for you.", UiTheme.Muted);
                    UiTheme.Gap(10);
                    UiTheme.Field("Room code", "##room-code", ref joinCode, 12, "Enter your room code");
                    CardActionPosition();
                    using (ImRaii.Disabled(client?.Connected != true || string.IsNullOrWhiteSpace(joinCode)))
                        if (UiTheme.Button("Take a seat", ImGui.GetContentRegionAvail().X, primary: true)) Send(new("join", Text: joinCode));
                }
            }
            ImGui.TableNextColumn();
            using (var card = new UiTheme.PaperPanel("make-card", new Vector2(0, UiTheme.S(250)), note: UiTheme.StickyRose))
            {
                if (card.Success)
                {
                    UiAssets.Glyph(FontAwesomeIcon.PaintBrush, UiTheme.Brass);
                    art.Heading("Make Room");
                    UiTheme.Wrapped("Set the pace, send an invitation, and make a little art together.", UiTheme.Muted);
                    UiTheme.Gap(10);
                    UiTheme.Caption("90 seconds  /  2 rounds");
                    UiTheme.Caption("Your room. Your rules.");
                    UiTheme.Gap(5);
                    CardActionPosition();
                    using (ImRaii.Disabled(client?.Connected != true))
                        if (UiTheme.Button("Set up a room", ImGui.GetContentRegionAvail().X)) screen = "create";
                }
            }
            ImGui.TableNextColumn();
            using (var card = new UiTheme.PaperPanel("public-card", new Vector2(0, UiTheme.S(250)), note: UiTheme.StickySage))
            {
                if (card.Success)
                {
                    UiAssets.Glyph(FontAwesomeIcon.Users, UiTheme.Brass);
                    art.Heading("Public Rooms");
                    UiTheme.Wrapped("A fresh page is better with company. Find a room and say hello.", UiTheme.Muted);
                    UiTheme.Gap(10);
                    UiTheme.Caption("Browse open invitations");
                    UiTheme.Caption("Meet the other artists.");
                    UiTheme.Gap(5);
                    CardActionPosition();
                    using (ImRaii.Disabled(client?.Connected != true))
                        if (UiTheme.Button("Find a room", ImGui.GetContentRegionAvail().X)) { screen = "public"; Send(new("list")); }
                }
            }
        }
        finally { ImGui.EndTable(); }
        UiTheme.Gap(8);
        if (profile is not null)
        {
            ImGui.Image(avatars.Get(profile).Handle, UiTheme.V(42, 42));
            ImGui.SameLine(); ImGui.BeginGroup();
            ImGui.TextUnformatted(profile.DisplayName);
            UiTheme.Caption("Your sketchbook, your signature.");
            ImGui.EndGroup();
        }
        else UiTheme.Wrapped("Connect to your server to load your profile and join the drawing room.", UiTheme.Muted);
    }

    private void CreateRoomScreen()
    {
        UiStationery.PageMark("02 / an open invitation"); art.Heading("Make a little room");
        UiTheme.Gap(4);
        if (!ImGui.BeginTable("room-setup-pages", 2, ImGuiTableFlags.NoPadOuterX)) return;
        try
        {
            ImGui.TableSetupColumn("Form", ImGuiTableColumnFlags.WidthStretch, 2.3f);
            ImGui.TableSetupColumn("Note", ImGuiTableColumnFlags.WidthStretch, 1);
            ImGui.TableNextColumn();
            using (var card = new UiTheme.PaperPanel("room-settings", Vector2.Zero))
            {
                if (card.Success)
                {
                    UiTheme.Field("Name your drawing room", "##room-name", ref roomTitle, 40, "A name for your little gathering");
                    UiTheme.Gap(5);
                    UiRoomSettings.Draw(ref seconds, ref rounds, ref hints, ref capacity);
                    UiTheme.Gap(5); UiTheme.Rule();
                    ImGui.Checkbox("List this room in Public Rooms", ref publicRoom);
                    UiTheme.Wrapped(publicRoom
                        ? "Anyone on this server sees the room name and artist count. Your profile is visible after they join."
                        : "By invitation. Share the code with your friends.", UiTheme.Muted);
                    UiTheme.Gap(6);
                    using (ImRaii.Disabled(client?.Connected != true || !Protocol.ValidName(roomTitle)))
                        if (UiTheme.Button("Open the room", UiTheme.S(170), primary: true))
                            Send(new("create", Options: new(roomTitle, publicRoom, Math.Clamp(seconds, 15, 300), Math.Clamp(rounds, 1, 5), Math.Clamp(hints, 0, 300), Math.Clamp(capacity, 2, 16))));
                    ImGui.SameLine(); if (UiTheme.Button("Back", 0, quiet: true)) screen = "menu";
                }
            }
            ImGui.TableNextColumn();
            using var note = new UiTheme.PaperPanel("room-invitation-note", new Vector2(0, UiTheme.S(275)), note: UiTheme.StickyYellow);
            if (!note.Success) return;
            UiStationery.PageMark("a note from Mog");
            var spot = ImGui.GetCursorScreenPos();
            ImGui.Dummy(UiTheme.V(70, 74));
            UiStationery.Moogle(spot + UiTheme.V(35, 46), UiTheme.S(23));
            UiTheme.Wrapped("Bring a friend. Leave room for a little silliness.");
            UiTheme.Gap(5);
            UiTheme.Wrapped("Everyone gets a turn at the page. A round ends when every artist has drawn once.", UiTheme.Muted);
        }
        finally { ImGui.EndTable(); }
    }

    private static void CardActionPosition()
    {
        float bottom = ImGui.GetWindowSize().Y - ImGui.GetStyle().WindowPadding.Y - UiTheme.S(28);
        if (ImGui.GetCursorPosY() < bottom) ImGui.SetCursorPosY(bottom);
    }

    private void PublicRooms()
    {
        art.Heading("Public Rooms");
        ImGui.SameLine();
        if (UiTheme.Button("Refresh", 0, quiet: true)) Send(new("list"));
        using var panel = new UiTheme.PaperPanel("public-list", Vector2.Zero);
        if (!panel.Success) return;
        if (publicRooms.Length == 0)
        {
            UiTheme.Gap(10); art.Mascot(56); UiTheme.Gap(6);
            art.Heading("The next room could be yours.");
            UiTheme.Wrapped("No public invitations just yet. Open a room and welcome the next artist.", UiTheme.Muted);
            if (UiTheme.Button("Make a room", 0, primary: true)) screen = "create";
            return;
        }
        foreach (var listed in publicRooms)
        {
            using var id = ImRaii.PushId(listed.Code);
            if (!ImGui.BeginTable("room", 3)) continue;
            try
            {
                ImGui.TableSetupColumn("Room", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Players", ImGuiTableColumnFlags.WidthFixed, UiTheme.S(110));
                ImGui.TableSetupColumn("Join", ImGuiTableColumnFlags.WidthFixed, UiTheme.S(100));
                ImGui.TableNextColumn();
                UiTheme.Ellipsis(listed.Title, ImGui.GetContentRegionAvail().X);
                UiTheme.Caption(listed.Phase == "drawing" || listed.Phase == "intermission" ? "A match is in progress" : "Ready for company");
                ImGui.TableNextColumn(); ImGui.TextUnformatted($"{listed.Players} / {listed.MaxPlayers} artists");
                ImGui.TableNextColumn();
                using (ImRaii.Disabled(client?.Connected != true || listed.Phase is not ("waiting" or "results") || listed.Players >= listed.MaxPlayers))
                    if (UiTheme.Button("Join", UiTheme.S(80))) Send(new("join", Text: listed.Code));
            }
            finally { ImGui.EndTable(); }
            UiTheme.Rule();
        }
    }

    private void Game()
    {
        var state = room!;
        bool host = state.HostId == device?.AccountId;
        int online = state.Players.Count(p => p.Connected);
        if (!ImGui.BeginTable("room-caption-compact", 2, ImGuiTableFlags.NoPadOuterX)) return;
        try
        {
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Invitation", ImGuiTableColumnFlags.WidthFixed, UiTheme.S(300));
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); UiTheme.Ellipsis(state.Options.Title, ImGui.GetContentRegionAvail().X);
            ImGui.TableNextColumn();
            UiTheme.Caption("ROOM " + state.Code);
            ImGui.SameLine(); if (UiTheme.Button("Copy code", 0, quiet: true)) ImGui.SetClipboardText(state.Code);
            ImGui.SameLine(); if (UiTheme.Button("Leave", 0, quiet: true)) { Send(new("leave")); return; }
        }
        finally { ImGui.EndTable(); }

        if (state.Phase is "waiting" or "results")
        {
            if (host)
            {
                using (ImRaii.Disabled(online < 2 || client?.Connected != true))
                    if (UiTheme.Button(state.Phase == "results" ? "Play another match" : "Start drawing", 0, primary: true)) Send(new("start"));
                ImGui.SameLine(); ImGui.AlignTextToFramePadding(); UiTheme.Caption(online < 2 ? "Invite one more artist to begin." : $"{online} artists are ready.");
            }
            else UiTheme.Caption("The host will start the match when everyone is ready.");
        }
        else DrawTurnHeader(state);
        float width = ImGui.GetContentRegionAvail().X;
        float height = Math.Max(UiTheme.S(120), ImGui.GetContentRegionAvail().Y - UiTheme.S(6));
        if (!ImGui.BeginTable("play-layout-compact", 3, ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX, new Vector2(0, height))) return;
        try
        {
            ImGui.TableSetupColumn("Artists", ImGuiTableColumnFlags.WidthFixed, UiTheme.S(width < UiTheme.S(1050) ? 132 : 150));
            ImGui.TableSetupColumn("The page", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Conversation", ImGuiTableColumnFlags.WidthFixed, UiTheme.S(width < UiTheme.S(1050) ? 200 : 220));
            ImGui.TableNextColumn(); DrawPlayers(state, height - UiTheme.S(6));
            ImGui.TableNextColumn(); DrawBoard(state, height - UiTheme.S(6));
            ImGui.TableNextColumn(); ChatScreen(state, height - UiTheme.S(6));
        }
        finally { ImGui.EndTable(); }
    }

    private void DrawTurnHeader(RoomView state)
    {
        int left = Math.Max(0, (int)Math.Ceiling((state.Deadline - (DateTimeOffset.UtcNow + serverOffset)).TotalSeconds));
        if (!ImGui.BeginTable("turn-header-compact", 3, ImGuiTableFlags.NoPadOuterX)) return;
        try
        {
            ImGui.TableSetupColumn("Round", ImGuiTableColumnFlags.WidthFixed, UiTheme.S(132));
            ImGui.TableSetupColumn("Word", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, UiTheme.S(100));
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); UiTheme.Caption($"Round {state.Round} / {state.Options.Rounds}");
            ImGui.TableNextColumn();
            art.Heading(state.Word ?? string.Join(' ', state.Hint.ToUpperInvariant().ToCharArray()));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(state.Phase == "intermission" ? "The word was" : state.DrawerId == device?.AccountId ? "Your word to draw" : "Guess the word");
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding();
            ImGui.TextColored(left <= 15 && state.Phase == "drawing" ? UiTheme.Rose : UiTheme.Ink, (state.Phase == "intermission" ? "Next: " : "") + left + "s");
        }
        finally { ImGui.EndTable(); }
    }

    private void DrawPlayers(RoomView state, float height)
    {
        using var panel = new UiTheme.PaperPanel("artists", new Vector2(0, height), compact: true, note: UiTheme.StickyYellow);
        if (!panel.Success) return;
        UiTheme.Caption($"ARTISTS / {state.Players.Length}");
        int rank = 0;
        foreach (var player in state.Players.OrderByDescending(p => p.Score))
        {
            var texture = avatars.Get(player.Profile);
            using var id = ImRaii.PushId(player.Profile.Id.ToString());
            rank++;
            ImGui.Image(texture.Handle, UiTheme.V(26, 26));
            ImGui.SameLine(); ImGui.BeginGroup();
            UiTheme.Ellipsis(player.Profile.DisplayName, Math.Max(UiTheme.S(35), ImGui.GetContentRegionAvail().X));
            ImGui.TextColored(player.Solved ? UiTheme.Green : UiTheme.Muted, $"{player.Score:N0} pts");
            ImGui.EndGroup();
            string role = !player.Connected ? "Reconnecting" : player.IsDrawer ? "At the drawing board" : player.Solved ? "Guessed it" : player.IsHost ? "Room host" : state.Phase == "results" && rank == 1 ? "First place" : "";
            if (role.Length > 0) UiTheme.Wrapped(role, UiTheme.Muted);
            if (player.Profile.Id != device?.AccountId)
            {
                if (ToolAction(muted.Contains(player.Profile.Id) ? FontAwesomeIcon.VolumeMute : FontAwesomeIcon.VolumeUp, muted.Contains(player.Profile.Id) ? "Unmute" : "Mute"))
                { if (!muted.Add(player.Profile.Id)) muted.Remove(player.Profile.Id); }
                if (state.HostId == device?.AccountId)
                { ImGui.SameLine(); if (ToolAction(FontAwesomeIcon.UserMinus, "Remove from room")) Send(new("kick", Text: player.Profile.Id.ToString())); }
            }
            UiTheme.Rule();
        }
    }

    private void DrawBoard(RoomView state, float height)
    {
        using var panel = new UiTheme.PaperPanel("drawing-board", new Vector2(0, height), compact: true);
        if (!panel.Success) return;
        bool canDraw = client?.Connected == true && state.Phase == "drawing" && state.DrawerId == device?.AccountId;
        if (canDraw) painting.Toolbar("game-tools");
        else UiTheme.Caption(state.Phase == "waiting" ? "THE DRAWING BOARD" : state.YouAreDone ? "You guessed it!" : state.Phase == "intermission" ? "Next artist coming up..." : state.Phase == "results" ? "That is a wrap, kupo." : "Watch the drawing. Guess in chat.");
        painting.Draw("game-canvas", canvas, ImGui.GetContentRegionAvail(), canDraw, operation =>
        {
            if (Send(new("draw", Draw: operation, TurnId: state.TurnId))) { canvas.Apply(operation); draws++; }
        });
        var size = new Vector2(MathF.Round(painting.RenderSize.X), MathF.Round(painting.RenderSize.Y));
        if (size != lastCanvasSize && Environment.TickCount64 >= nextLayoutLog)
        {
            log.Write($"ui.canvas size={size.X}x{size.Y} scale={UiTheme.Scale:F2}");
            lastCanvasSize = size; nextLayoutLog = Environment.TickCount64 + 1000;
        }
    }

    private void ChatScreen(RoomView state, float height)
    {
        using var panel = new UiTheme.PaperPanel("chat-panel", new Vector2(0, height), compact: true, note: UiTheme.StickyBlue);
        if (!panel.Success) return;
        ImGui.TextUnformatted(state.YouAreDone ? "Done mode" : "Room chat");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(state.YouAreDone ? "Only the drawer and correct guessers see your messages." : "Talk and guess in the same chat.");
        UiTheme.Rule();
        using (var messages = ImRaii.Child("messages", new Vector2(0, -UiTheme.S(34)), false))
        {
            if (messages.Success)
            {
                foreach (var line in chat)
                {
                    if (line.System)
                    { UiTheme.Wrapped(line.Text, UiTheme.Green); UiTheme.Gap(3); continue; }
                    using (ImRaii.PushColor(ImGuiCol.Text, line.Done ? UiTheme.Rose : UiTheme.Brass)) UiTheme.Ellipsis(line.Name, ImGui.GetContentRegionAvail().X);
                    UiTheme.Wrapped(line.Text); UiTheme.Gap(4);
                }
                if (scrollChat) { ImGui.SetScrollHereY(1); scrollChat = false; }
            }
        }
        using (ImRaii.Disabled(client?.Connected != true))
        {
            ImGui.SetNextItemWidth(Math.Max(UiTheme.S(60), ImGui.GetContentRegionAvail().X - UiTheme.S(48)));
            if (focusChat) { ImGui.SetKeyboardFocusHere(); focusChat = false; }
            bool enter = ImGui.InputTextWithHint("##message", state.YouAreDone ? "Say something..." : "Talk or guess...", ref chatInput, 300, ImGuiInputTextFlags.EnterReturnsTrue);
            if (enter) ImGui.SetKeyboardFocusHere(-1);
            ImGui.SameLine(); bool click = ToolAction(FontAwesomeIcon.PaperPlane, "Send");
            if ((enter || click) && !string.IsNullOrWhiteSpace(chatInput))
            { Send(new("chat", Text: chatInput, TurnId: state.TurnId)); chatInput = ""; focusChat = true; }
        }
    }

    private static bool ToolAction(FontAwesomeIcon icon, string label, bool selected = false)
    {
        bool clicked;
        using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
        using (ImRaii.PushColor(ImGuiCol.Button, selected ? UiTheme.RoseTint : Vector4.Zero))
        using (ImRaii.PushColor(ImGuiCol.Text, selected ? UiTheme.Rose : UiTheme.Ink))
            clicked = ImGui.Button(icon.ToIconString() + "##" + label, UiTheme.V(28, 28));
        if (ImGui.IsItemHovered() || ImGui.IsItemFocused()) ImGui.SetTooltip(label);
        return clicked;
    }

    private void OpenProfile()
    {
        if (profile is null) { status = "Connect to load your profile first."; return; }
        displayName = profile.DisplayName;
        avatar.Load(profile.AvatarPixels ?? AvatarGenerator.Generate(profile.AvatarSeed));
        avatarReset = profile.AvatarPixels is null;
        screen = "profile";
    }

    private void ProfileScreen()
    {
        UiStationery.PageMark("03 / your signature"); art.Heading("A little face. Entirely yours.");
        UiTheme.Wrapped("Draw your own signature character, or keep the face we made for you.", UiTheme.Muted);
        UiTheme.Gap(10);
        if (!ImGui.BeginTable("profile-layout", 2)) return;
        try
        {
            ImGui.TableSetupColumn("Portrait", ImGuiTableColumnFlags.WidthStretch, 1.15f);
            ImGui.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthStretch, 1);
            ImGui.TableNextColumn();
            using (var editor = new UiTheme.PaperPanel("portrait-editor", Vector2.Zero))
            {
                if (editor.Success)
                {
                    UiTheme.Caption("YOUR CANVAS");
                    avatarEditor.Toolbar("avatar-tools");
                    avatarEditor.Draw("avatar-canvas", avatar, new Vector2(ImGui.GetContentRegionAvail().X,
                        Math.Max(UiTheme.S(100), ImGui.GetContentRegionAvail().Y - UiTheme.S(38))), true,
                        operation => { avatar.Apply(operation); avatarReset = false; });
                    UiTheme.Gap(5);
                    if (UiTheme.Button("Use my default avatar", 0, quiet: true) && profile is not null)
                    { avatar.Load(AvatarGenerator.Generate(profile.AvatarSeed)); avatarReset = true; }
                }
            }
            ImGui.TableNextColumn();
            using var card = new UiTheme.PaperPanel("profile-details", Vector2.Zero, note: UiTheme.StickyRose);
            if (!card.Success) return;
            UiTheme.Caption("A NAME TO REMEMBER");
            UiTheme.Field("Display name", "##profile-name", ref displayName, 40, "Your artist name");
            UiTheme.Wrapped("Character unverified. Your name and avatar are shared with the people in your room.", UiTheme.Muted);
            UiTheme.Gap(6);
            using (ImRaii.Disabled(savingProfile || client?.Connected != true || !Protocol.ValidName(displayName)))
                if (UiTheme.Button(savingProfile ? "Saving..." : "Save my profile", ImGui.GetContentRegionAvail().X, primary: true))
                {
                    savingProfile = Send(new("profile", Text: displayName, Avatar: avatarReset ? null : (byte[])avatar.Pixels.Clone(), ResetAvatar: avatarReset));
                    if (savingProfile) status = "Saving profile...";
                }
            UiTheme.Rule(); UiTheme.Caption("ACCOUNT RECOVERY");
            UiTheme.Wrapped("Lost your saved code? Replace it while this device still has access. Your previous code stops working and other devices are signed out.", UiTheme.Muted);
            using (ImRaii.Disabled(busy || device is null || client?.Connected != true))
                if (UiTheme.Button("Replace recovery code", ImGui.GetContentRegionAvail().X))
                {
                    var activeClient = client!; string token = device!.AccessToken;
                    Work(async () => await AcceptCredentials(await activeClient.RotateRecoveryAsync(token)), "Replacing recovery code");
                }
        }
        finally { ImGui.EndTable(); }
    }

    private void ServerSettings()
    {
        UiStationery.PageMark("the back cover"); art.Heading("Connection settings");
        UiTheme.Wrapped("Connect to the server where your friends are drawing. Profiles belong to the server that created them.", UiTheme.Muted);
        UiTheme.Gap(10);
        using var card = new UiTheme.PaperPanel("connection-settings", Vector2.Zero);
        if (!card.Success) return;
        using (ImRaii.Disabled(busy))
        {
            UiTheme.Field("Server address", "##server-url", ref serverUrl, 256, "https://localhost:7443");
            if (UiTheme.Button("Use this server", 0, primary: true)) ConfigureClient();
            ImGui.SameLine();
            if (device is not null && client?.Connected != true)
            { if (UiTheme.Button("Connect")) Connect(); }
            else if (client?.Connected == true && UiTheme.Button("Disconnect", 0, quiet: true))
            { reconnect = false; Work(async () => { if (client is not null) await client.DisconnectAsync(); }, "Disconnecting"); }
        }
        UiTheme.Wrapped("For testing on this PC, run Start-My-Server.cmd and use https://localhost:7443. Friends on another PC need your public HTTPS hostname.", UiTheme.Muted);
        UiTheme.Rule();
        bool reduceMotion = plugin.Config.ReduceMotion;
        if (ImGui.Checkbox("Reduce decorative motion", ref reduceMotion)) { plugin.Config.ReduceMotion = reduceMotion; plugin.Save(); }
        UiTheme.Caption("Keep the moogle pom still while moving the book.");
        bool standardFrame = plugin.Config.UseStandardFrame;
        if (ImGui.Checkbox("Use the standard window frame", ref standardFrame))
        { plugin.Config.UseStandardFrame = standardFrame; plugin.Save(); }
        UiTheme.Wrapped("Paper mode: drag the moogle/title, use the pin and close buttons, and resize from the edges. The standard frame restores Dalamud's window menu.", UiTheme.Muted);
        UiTheme.Rule();
        UiTheme.Caption("DIAGNOSTICS");
        ImGui.TextUnformatted($"Connection: {(client?.Connected == true ? "online" : "offline")}   /   Events received: {received}   /   Drawing messages: {draws}");
        UiTheme.Wrapped(log.FilePath, UiTheme.Muted);
        if (UiTheme.Button("Copy log path")) ImGui.SetClipboardText(log.FilePath);
        ImGui.SameLine(); using (ImRaii.Disabled(room is null || client?.Connected != true))
            if (UiTheme.Button("Resync room", 0, quiet: true)) Send(new("sync"));
        UiTheme.Gap(5);
        UiTheme.Wrapped("Diagnostics exclude recovery codes, login credentials, answer words, chat text, and drawing contents.", UiTheme.Muted);
        if (UiTheme.Button(newRecovery.Length > 0 ? "Back" : screen == "welcome" ? "Back to welcome" : "Back to play", 0, quiet: true)) ShowDebug = false;
    }

    private static string SafeCommand(string? command) => command is "start" or "join" or "create" or "draw" or "profile" or "chat" or "sync" or "kick" or "leave" or "list" ? command : "unknown";
    private static string SafeReason(string? error) => error switch
    {
        "At least two connected players are needed." => "players_required",
        "Room is full." => "room_full",
        "Room not found." => "room_not_found",
        "Slow down; message limit reached." => "rate_limited",
        "Only the room host can do that." => "host_required",
        "Drawing is only available during your current turn." => "drawing_not_allowed",
        "A match is already running." => "match_running",
        _ => "other",
    };
}
