using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Kupopop.Shared;

namespace Kupopop;

public sealed partial class MainWindow
{
    private bool returningArtist, focusChat;
    private Vector2 lastCanvasSize;
    private long nextLayoutLog;
    private readonly PomMotion pom = new();
    private readonly UiAspectConstraint aspect = new();

    private static string UiVersion => typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "dev";
    internal bool IsDrawingRoom => ConnectionReady && newRecovery.Length == 0 && !ShowDebug && screen == "game" && room is not null;

    public override void PreDraw()
    {
        var display = ImGui.GetIO().DisplaySize;
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        if (!ShowDebug) Flags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground;
        ForceMainWindow = true;
        AllowBackgroundBlur = ShowDebug;
        AllowClickthrough = ShowDebug;
        DisableFadeInFadeOut = !ShowDebug;
        // Native movement owns the entire window tree and viewport. Never fight
        // it with custom drag zones or a per-frame position controller. Hiding
        // the title bar leaves ImGui's normal empty-space dragging intact.
        Position = null;
        // Supply the native callback directly: WindowSystem's optional bounds
        // would otherwise replace it after PreDraw. Scale exactly once here.
        SizeConstraints = null;
        aspect.Apply(display, UiTheme.Scale);
    }

    public override void Draw()
    {
        var position = ImGui.GetWindowPos();
        pom.Step(position.X + position.Y * 0.35f, ImGui.GetIO().DeltaTime, !plugin.Config.ReduceMotion);
        UiStationery.Book(position, position + ImGui.GetWindowSize(), ShowDebug ? ImGui.GetFrameHeight() : 0);
        DrawHeader();
        UiTheme.Rule();
        using (var transparent = ImRaii.PushColor(ImGuiCol.ChildBg, Vector4.Zero))
        using (var body = ImRaii.Child("page-content", new Vector2(0, Math.Max(UiTheme.S(100), ImGui.GetContentRegionAvail().Y - UiTheme.S(28))), false,
            IsDrawingRoom ? ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse : ImGuiWindowFlags.None))
        {
            if (body.Success)
            {
                if (!ConnectionReady) ConnectingScreen();
                else if (newRecovery.Length > 0) RecoveryScreen();
                else if (ShowDebug) Preferences();
                else if (screen == "welcome") Welcome();
                else if (screen == "profile") ProfileScreen();
                else if (screen == "friends") FriendsScreen();
                else if (screen == "leaderboards") LeaderboardsScreen();
                else if (screen == "suggestions") SuggestionsScreen();
                else if (screen == "game" && room is not null) Game();
                else if (screen == "public") PublicRooms();
                else if (screen == "create") CreateRoomScreen();
                else Menu();
            }
        }
        UiChrome.Footer(position, position + ImGui.GetWindowSize(), "v" + UiVersion, IsPinned);
        DrawToasts();
        SharedProfilePopup();
    }

    private void DrawHeader()
    {
        if (!ImGui.BeginTable("brand-paper-controls", 4, ImGuiTableFlags.NoPadOuterX)) return;
        try
        {
            ImGui.TableSetupColumn("Brand", ImGuiTableColumnFlags.WidthFixed, UiTheme.S(215));
            ImGui.TableSetupColumn("Navigation", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Service status", ImGuiTableColumnFlags.WidthFixed, UiTheme.S(114));
            ImGui.TableSetupColumn("Paper controls", ImGuiTableColumnFlags.WidthFixed, UiTheme.S(ShowDebug ? 32 : 100));
            ImGui.TableNextColumn();
            var face = ImGui.GetCursorScreenPos();
            ImGui.Dummy(UiTheme.V(211, 34));
            var afterBrand = ImGui.GetCursorScreenPos();
            UiStationery.Moogle(face + UiTheme.V(20, 23), UiTheme.S(14), pom.Angle);
            ImGui.SetCursorScreenPos(face + UiTheme.V(49, 0));
            ImGui.BeginGroup();
            art.Heading("Kupo Skribbl");
            ImGui.EndGroup();
            ImGui.SetCursorScreenPos(afterBrand);
            ImGui.TableNextColumn();
            UiTheme.Gap(1);
            if (ConnectionReady && newRecovery.Length == 0 && screen != "welcome") Navigation();
            ImGui.TableNextColumn();
            bool online = ConnectionReady;
            UiTheme.Gap(1);
            ImGui.AlignTextToFramePadding();
            UiAssets.Glyph(FontAwesomeIcon.Circle, online ? UiTheme.Green : UiTheme.Muted);
            ImGui.SameLine();
            ImGui.TextUnformatted(online ? "Connected" : "Connecting...");
            ImGui.TableNextColumn(); UiTheme.Gap(1);
            if (ToolAction(plugin.Config.DarkMode ? FontAwesomeIcon.Sun : FontAwesomeIcon.Moon,
                plugin.Config.DarkMode ? "Use light paper" : "Use dark paper"))
            { plugin.Config.DarkMode = !plugin.Config.DarkMode; plugin.Save(); }
            if (!ShowDebug)
            {
                ImGui.SameLine();
                if (ToolAction(FontAwesomeIcon.Thumbtack, IsPinned ? "Unpin book" : "Pin book", IsPinned)) IsPinned = !IsPinned;
                ImGui.SameLine();
                if (ToolAction(FontAwesomeIcon.Times, "Close book"))
                { IsOpen = false; SynchronizeVisibility(); }
            }
        }
        finally { ImGui.EndTable(); }
    }

    private void Navigation()
    {
        string play = room is null ? "Play" : "Your room";
        var start = ImGui.GetCursorScreenPos();
        float textWidth = ImGui.CalcTextSize(play).X + ImGui.CalcTextSize("ProfileFriendsLeaderboards").X;
        float tabPadding = Math.Clamp((ImGui.GetContentRegionAvail().X - textWidth - UiTheme.S(12)) / 4, UiTheme.S(8), UiTheme.S(26));
        float width = textWidth + tabPadding * 4 + UiTheme.S(8);
        ImGui.GetWindowDrawList().AddRectFilled(start - UiTheme.V(3, 3), start + new Vector2(width, UiTheme.S(33)),
            UiTheme.C(UiPresentation.Alpha(UiTheme.Inset, 0.55f)), UiTheme.S(7));
        if (UiTheme.Tab(play, !ShowDebug && screen is not ("profile" or "friends" or "leaderboards"), tabPadding))
        { ShowDebug = false; screen = room is null ? "menu" : "game"; }
        ImGui.SameLine(0, UiTheme.S(2));
        using (ImRaii.Disabled(profile is null))
            if (UiTheme.Tab("Profile", !ShowDebug && screen == "profile", tabPadding)) { ShowDebug = false; OpenProfile(); }
        ImGui.SameLine(0, UiTheme.S(2));
        using (ImRaii.Disabled(profile is null))
            if (UiTheme.Tab("Friends", !ShowDebug && screen == "friends", tabPadding)) OpenFriends();
        ImGui.SameLine(0, UiTheme.S(2));
        using (ImRaii.Disabled(profile is null))
            if (UiTheme.Tab("Leaderboards", !ShowDebug && screen == "leaderboards", tabPadding))
            { ShowDebug = false; screen = "leaderboards"; RequestLeaderboard(); }
    }

    private void ConnectingScreen()
    {
        UiTheme.Gap(40);
        UiStationery.PageMark("opening your sketchbook");
        art.Heading("Trying to connect");
        UiTheme.Gap(10);
        UiTheme.Wrapped(connectionStatus, UiTheme.Muted);
        if (!busy && client is not null && connectionRetry.Attempts > 0)
        {
            int remaining = (int)Math.Max(1, (connectionRetry.NextAttempt - Environment.TickCount64 + 999) / 1000);
            UiTheme.Caption($"Next attempt in {remaining}s. You will continue automatically once connected.");
        }
    }

    private void Welcome()
    {
        var available = ImGui.GetContentRegionAvail();
        var size = new Vector2(Math.Min(available.X, UiTheme.S(480)), Math.Min(available.Y, UiTheme.S(returningArtist ? 470 : 350)));
        ImGui.SetCursorScreenPos(ImGui.GetCursorScreenPos() + Vector2.Max(Vector2.Zero, (available - size) / 2));
        using var panel = new UiTheme.PaperPanel("welcome-card", size);
        if (!panel.Success) return;
        UiTheme.Caption("Welcome, kupo.");
        UiTheme.Gap(6);
        using (ImRaii.Disabled(busy || client is null))
        {
            UiTheme.Field("Display name", "##new-name", ref displayName, 40, "Your character or artist name");

            UiTheme.Wrapped("Recommended: record lifetime stats, share your profile with room partners and friends, and check for invitations once a minute while closed. Recorded stats appear anonymously with your portrait on leaderboards unless you opt out. Sharing or a recognisable portrait can reveal plugin use. You can hide or disable each feature later.", UiTheme.Muted);
            using (ImRaii.Disabled(!Protocol.ValidName(displayName)))
            {
                if (UiTheme.Button(busy ? "Creating your profile..." : "Create & enable all three", ImGui.GetContentRegionAvail().X, primary: true)) CreateAccount(false, enableAll: true);
                if (UiTheme.Button("Create a private profile", ImGui.GetContentRegionAvail().X, quiet: true)) CreateAccount(false);
            }
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
            UiPortrait.Draw(avatars.Get(profile).Handle, UiTheme.V(64, 64));
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
        var available = ImGui.GetContentRegionAvail();
        float width = Math.Min(available.X, UiTheme.S(900));
        var origin = ImGui.GetCursorScreenPos() + new Vector2(Math.Max(0, (available.X - width) / 2), Math.Max(0, (available.Y - UiTheme.S(310)) / 2));
        ImGui.SetCursorScreenPos(origin);
        if (!ImGui.BeginTable("play-actions", 3, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX, new Vector2(width, 0))) return;
        try
        {
            ImGui.TableNextColumn();
            using (var card = new UiTheme.PaperPanel("join-card", new Vector2(0, UiTheme.S(250)), note: ActiveInvitations().Length > 0 ? UiTheme.InvitationPaper : UiTheme.StickyYellow, scrollable: false))
            {
                if (card.Success)
                {
                    var invitation = ActiveInvitations().FirstOrDefault();
                    if (invitation is not null) UiTheme.InvitationGlow(ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize());
                    art.Heading(invitation is null ? "Join via code" : "You're invited!");
                    UiTheme.Wrapped(invitation is null ? "Enter a code from your friend below to play." : invitation.From.DisplayName + " invited you to " + invitation.RoomTitle, UiTheme.Muted);
                    UiTheme.Gap(10);
                    if (invitation is null) UiTheme.Field("Room code", "##room-code", ref joinCode, 12, "Enter your room code");
                    else if (UiTheme.Button("Dismiss invitation", 0, quiet: true)) Send(new("dismiss-invite", TargetId: invitation.Id));
                    CardActionPosition();
                    using (ImRaii.Disabled(client?.Connected != true || invitation is null && string.IsNullOrWhiteSpace(joinCode)))
                        if (UiTheme.NoteButton(invitation is null ? "Join room" : "Join invited game", ImGui.GetContentRegionAvail().X, invitation is null ? UiTheme.StickyYellow : UiTheme.InvitationPaper, primary: true))
                            Send(invitation is null ? new("join", Text: joinCode) : new("accept-invite", TargetId: invitation.Id));
                }
            }
            ImGui.TableNextColumn();
            using (var card = new UiTheme.PaperPanel("make-card", new Vector2(0, UiTheme.S(250)), note: UiTheme.StickyRose, scrollable: false))
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
                        if (UiTheme.NoteButton("Set up a room", ImGui.GetContentRegionAvail().X, UiTheme.StickyRose)) OpenRoomSetup();
                }
            }
            ImGui.TableNextColumn();
            using (var card = new UiTheme.PaperPanel("public-card", new Vector2(0, UiTheme.S(250)), note: UiTheme.StickySage, scrollable: false))
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
                        if (UiTheme.NoteButton("Find a room", ImGui.GetContentRegionAvail().X, UiTheme.StickySage)) { screen = "public"; Send(new("list")); }
                }
            }
        }
        finally { ImGui.EndTable(); }
        UiTheme.Gap(8);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + UiTheme.S(6), ImGui.GetCursorScreenPos().Y));
        if (profile is not null)
        {
            UiPortrait.Draw(avatars.Get(profile).Handle, UiTheme.V(42, 42));
            ImGui.SameLine(); ImGui.BeginGroup();
            ImGui.TextUnformatted(profile.DisplayName);
            UiTheme.Caption("Your sketchbook, your signature.");
            ImGui.EndGroup();
        }
        else UiTheme.Wrapped("Your profile will appear here when it has loaded.", UiTheme.Muted);
        ImGui.SameLine();
        float suggestionWidth = ImGui.CalcTextSize("Suggestions").X + UiTheme.S(36);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + width - UiTheme.S(6) - suggestionWidth, ImGui.GetCursorScreenPos().Y));
        if (UiTheme.Button("Suggestions", suggestionWidth, primary: true)) { ShowDebug = false; screen = "suggestions"; }
    }

    private void CreateRoomScreen()
    {
        bool editing = room is not null;
        if (PageHeading(editing ? "Room settings" : "Host Room")) return;
        var tabs = ImGui.GetCursorScreenPos();
        float tabHeight = UiTheme.S(34);
        ImGui.GetWindowDrawList().AddLine(tabs + new Vector2(0, tabHeight), tabs + new Vector2(ImGui.GetContentRegionAvail().X, tabHeight), UiTheme.C(UiTheme.Line));
        if (UiTheme.PanelTab("Basic", roomSetupPage == 0)) roomSetupPage = 0;
        ImGui.SameLine(0, UiTheme.S(3)); if (UiTheme.PanelTab("Advanced", roomSetupPage == 1)) roomSetupPage = 1;
        ImGui.SameLine(0, UiTheme.S(3)); if (UiTheme.PanelTab("Word Lists", roomSetupPage == 2)) roomSetupPage = 2;
        ImGui.SameLine(0, UiTheme.S(3)); if (UiTheme.PanelTab("Custom Words", roomSetupPage == 3)) roomSetupPage = 3;
        ImGui.SetCursorScreenPos(tabs + new Vector2(0, tabHeight));
        using var card = new UiTheme.PaperPanel("room-settings", Vector2.Zero, scrollable: false, flush: true, attachedTabs: true);
        if (!card.Success) return;
        using (ImRaii.PushColor(ImGuiCol.ChildBg, Vector4.Zero))
        using (var body = ImRaii.Child("room-settings-page", new Vector2(0, -UiTheme.S(46)), false))
        {
            if (body.Success)
            {
                if (roomSetupPage == 1) UiRoomSettings.Advanced(ref hints, ref wordChoiceCount, ref voteKickEnabled, ref voteSkipEnabled, ref customWordsEnabled);
                else if (roomSetupPage == 2) WordListPage();
                else if (roomSetupPage == 3) CustomWordsPage();
                else
                {
                    UiTheme.Field("Room name", "##room-name", ref roomTitle, 40, "Name your drawing room");
                    UiTheme.Gap(5);
                    UiRoomSettings.Basic(ref seconds, ref rounds, ref capacity);
                    UiTheme.Gap(12);
                    ImGui.Checkbox("Public lobby", ref publicRoom);
                    if (!editing) ImGui.Checkbox("Invite friends after creating", ref inviteAfterCreate);
                    UiTheme.Wrapped(publicRoom
                        ? "Anyone can see the room name and player count. Your profile is visible after they join."
                        : "Private room. Only people with your code can join.", UiTheme.Muted);
                }
            }
        }
        UiTheme.Gap(4);
        bool validCustom = TryCustomWords(out int customCount);
        using (ImRaii.Disabled(savingRoomSettings || client?.Connected != true || !Protocol.ValidName(roomTitle) || !validCustom || selectedWordLists.Count == 0 && customCount < wordChoiceCount))
            if (UiTheme.Button(editing ? "Save settings" : "Host Room", UiTheme.S(170), primary: true))
            {
                bool sent = Send(new(editing ? "configure" : "create", Options: new(roomTitle, publicRoom, Math.Clamp(seconds, 15, 300), Math.Clamp(rounds, 1, 5),
                    Math.Clamp(hints, 0, 300), Math.Clamp(capacity, 2, 16), selectedWordLists.ToArray(),
                    Math.Clamp(wordChoiceCount, 1, 8), voteKickEnabled, voteSkipEnabled, customWordsEnabled), CustomWords: customWordsText));
                savingRoomSettings = editing && sent;
            }
        ImGui.SameLine(); ImGui.AlignTextToFramePadding(); UiTheme.Caption($"{selectedWordLists.Count} word lists selected");
    }

    private void OpenRoomSetup(bool publicByDefault = false) { roomSetupPage = 0; customWordsText = ""; publicRoom = publicByDefault; screen = "create"; }

    private bool PageHeading(string title, bool refresh = false)
    {
        bool back = false;
        if (ImGui.BeginTable("page-heading", 2, ImGuiTableFlags.NoPadOuterX))
        {
            try
            {
                float actions = ImGui.CalcTextSize(room is null ? "Back to home" : "Back to room").X + UiTheme.S(36);
                if (refresh) actions += ImGui.CalcTextSize("Refresh").X + UiTheme.S(24);
                ImGui.TableSetupColumn("Title", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, actions);
                ImGui.TableNextColumn(); art.Heading(title);
                ImGui.TableNextColumn(); back = BackButton();
                if (refresh)
                {
                    ImGui.SameLine(); if (UiTheme.Button("Refresh", 0, quiet: true)) Send(new("list"));
                }
            }
            finally { ImGui.EndTable(); }
        }
        UiTheme.Gap(6);
        return back;
    }

    private bool BackButton()
    {
        if (!UiTheme.BackButton(room is null ? "Back to home" : "Back to room")) return false;
        ShowDebug = false; screen = room is null ? "menu" : "game"; return true;
    }

    private void EditRoomSettings()
    {
        if (room is null) return;
        var options = room.Options;
        roomTitle = options.Title; publicRoom = options.IsPublic; seconds = options.TurnSeconds;
        rounds = options.Rounds; hints = options.HintSeconds; capacity = options.MaxPlayers;
        wordChoiceCount = options.WordChoiceCount; voteKickEnabled = options.VoteKickEnabled;
        voteSkipEnabled = options.VoteSkipEnabled; customWordsEnabled = options.CustomWordsEnabled;
        customWordsText = roomCustomWords;
        selectedWordLists.Clear();
        foreach (var id in options.WordLists ?? []) selectedWordLists.Add(id);
        wordListsChosen = true; roomSetupPage = 0; screen = "create";
    }

    private static void CardActionPosition()
    {
        float bottom = ImGui.GetCursorPosY() + ImGui.GetContentRegionAvail().Y - UiTheme.S(30);
        if (ImGui.GetCursorPosY() < bottom) ImGui.SetCursorPosY(bottom);
    }

    private void WordListPage()
    {
        if (UiTheme.Button("Normal lists", 0, quiet: true))
        {
            selectedWordLists.Clear();
            foreach (var list in wordLists.Where(l => l.DefaultEnabled)) selectedWordLists.Add(list.Id);
            wordListsChosen = true;
        }
        ImGui.SameLine();
        if (UiTheme.Button("All", 0, quiet: true)) { foreach (var list in wordLists) selectedWordLists.Add(list.Id); wordListsChosen = true; }
        ImGui.SameLine();
        if (UiTheme.Button("None", 0, quiet: true)) { selectedWordLists.Clear(); wordListsChosen = true; }
        UiTheme.Gap(5);
        foreach (var group in wordLists.GroupBy(l => l.Category))
        {
            using var categoryId = ImRaii.PushId(group.Key);
            var members = group.ToArray();
            int selected = members.Count(l => selectedWordLists.Contains(l.Id));
            bool included = selected > 0;
            if (ImGui.Checkbox("##category-enabled", ref included))
            {
                bool hasNormal = members.Any(l => l.DefaultEnabled);
                foreach (var list in members)
                { if (included && (list.DefaultEnabled || !hasNormal)) selectedWordLists.Add(list.Id); else selectedWordLists.Remove(list.Id); }
                wordListsChosen = true;
            }
            if (ImGui.IsItemHovered()) UiTheme.Tooltip("Include or exclude this category. Enabling it selects its normal lists; advanced lists stay optional.");
            ImGui.SameLine();
            bool collapsed = collapsedWordCategories.Contains(group.Key);
            if (UiTheme.CategoryHeader(group.Key, $"{selected}/{members.Length} lists · {members.Sum(l => l.Count):N0} words", !collapsed))
            { if (collapsed) collapsedWordCategories.Remove(group.Key); else collapsedWordCategories.Add(group.Key); }
            if (collapsed) continue;
            int columns = ImGui.GetContentRegionAvail().X >= UiTheme.S(760) ? 3 : 2;
            if (!ImGui.BeginTable("word-list-buttons", columns, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX)) continue;
            try
            {
                foreach (var list in members)
                {
                    ImGui.TableNextColumn();
                    bool enabled = selectedWordLists.Contains(list.Id);
                    string detail = $"{list.Count} words" + (list.Complex ? " - Advanced" : "");
                    if (UiTheme.ListButton(list.Id, list.Name, detail, enabled))
                    { if (enabled) selectedWordLists.Remove(list.Id); else selectedWordLists.Add(list.Id); wordListsChosen = true; }
                    if (ImGui.IsItemHovered()) UiTheme.Tooltip(list.Name + "\n" + detail + (list.Complex ? "\nDisabled by default." : ""));
                }
            }
            finally { ImGui.EndTable(); }
            UiTheme.Gap(4);
        }
    }

    private void PublicRooms()
    {
        if (PageHeading("Browse Lobbies", refresh: true)) return;
        using var panel = new UiTheme.PaperPanel("public-list", Vector2.Zero);
        if (!panel.Success) return;
        if (publicRooms.Length == 0)
        {
            string invitation = "No rooms available, would you like to make your own?";
            float width = Math.Min(UiTheme.S(430), ImGui.GetContentRegionAvail().X);
            float textHeight = ImGui.CalcTextSize(invitation, false, width).Y;
            var origin = ImGui.GetCursorScreenPos() + new Vector2(Math.Max(0, (ImGui.GetContentRegionAvail().X - width) / 2), Math.Max(0, (ImGui.GetContentRegionAvail().Y - textHeight - UiTheme.S(52)) / 2));
            UiPresentation.DrawCenteredText(invitation, origin, new Vector2(width, textHeight));
            ImGui.SetCursorScreenPos(origin + new Vector2(Math.Max(0, (width - UiTheme.S(160)) / 2), textHeight + UiTheme.S(14)));
            if (UiTheme.Button("Host Room", UiTheme.S(160), primary: true)) OpenRoomSetup(publicByDefault: true);
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
                UiTheme.Caption(listed.Phase is "drawing" or "intermission" or "choosing" ? "A match is in progress" : "Ready for company");
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
        if (round.Length > 0) { ImGui.SameLine(0, UiTheme.S(6)); UiTheme.Caption(round); }
        if (playing)
        {
            ImGui.SetCursorScreenPos(top + new Vector2(layout.Artists + layout.Gap, 0));
            DrawTurnHeader(state, layout.Canvas);
        }
        float right = top.X + available.X;
        float actionWidth = ImGui.CalcTextSize(state.Code + "Copy codeLeave").X + UiTheme.S(44);
        ImGui.SetCursorScreenPos(new Vector2(right - actionWidth, top.Y));
        ImGui.AlignTextToFramePadding(); UiTheme.Caption(state.Code);
        ImGui.SameLine(0, UiTheme.S(3)); if (UiTheme.Button("Copy code", 0, quiet: true)) ImGui.SetClipboardText(state.Code);
        ImGui.SameLine(0, UiTheme.S(2)); if (UiTheme.Button("Leave", 0, quiet: true)) { Send(new("leave")); return; }
        ImGui.SetCursorScreenPos(top + new Vector2(0, headerHeight));
        if (startHeight > 0)
        {
            if (host)
            {
                using (ImRaii.Disabled(online < 2 || client?.Connected != true))
                    if (UiTheme.Button(state.Phase == "results" ? "Play again" : "Start drawing", 0, primary: true)) Send(new("start"));
                ImGui.SameLine(); if (UiTheme.Button("Room settings", 0, quiet: true)) { EditRoomSettings(); return; }
                ImGui.SameLine(); if (UiTheme.Button("Invite friends", 0, quiet: true)) { OpenFriends(inviting: true); return; }
                if (online < 2) { ImGui.SameLine(); ImGui.AlignTextToFramePadding(); UiTheme.Caption("Invite another artist to begin."); }
            }
            else UiTheme.Caption("Waiting for the host to start.");
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
        if (state.Word is { } word) art.WordHeading(word, width - inset);
        else art.Hint(state.Hint, width);
        if (state.Word is not null && ImGui.IsItemHovered()) UiTheme.Tooltip(state.Phase == "intermission" ? "The word was" : "Your word to draw");
    }

    private void DrawPlayers(RoomView state, Vector2 size)
    {
        using var panel = new UiTheme.PaperPanel("artists", size, compact: true, note: UiTheme.StickyYellow, flush: true);
        if (!panel.Success) return;
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
            if (drawing && ImGui.IsItemHovered()) UiTheme.Tooltip("Current drawer");
            ImGui.SameLine(); ImGui.BeginGroup();
            UiTheme.Ellipsis(player.Profile.DisplayName, Math.Max(UiTheme.S(35), ImGui.GetContentRegionAvail().X));
            ImGui.TextColored(player.Solved ? UiTheme.Green : UiTheme.Muted, $"{player.Score:N0} pts");
            ImGui.EndGroup();
            ImGui.EndGroup();
            if (player.Profile.Id != device?.AccountId && ImGui.BeginPopupContextItem("artist-actions"))
            {
                SocialActions(player.Profile.Id);
                if (state.HostId == device?.AccountId && ImGui.MenuItem("Kick from room")) Send(new("kick", Text: player.Profile.Id.ToString()));
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
        painting.Draw("game-canvas", canvas, size, canDraw && !undoPending, operation =>
        {
            if (Send(new("draw", Draw: operation, TurnId: state.TurnId))) { canvas.Apply(operation); draws++; canUndoDrawing = true; }
        }, canDraw ? pointer => Send(new("cursor", TurnId: state.TurnId, Cursor: pointer)) : null);
        if (state.Phase == "choosing" && state.DrawerId == device?.AccountId) DrawWordChoices(state);
        if (state.Phase is "choosing" or "drawing" or "intermission") DrawCanvasTimer(state);
        if (canDraw) DrawCanvasTools(state);
        if (state.Phase == "drawing" && state.DrawerId != device?.AccountId) DrawCanvasVotes(state);
        if (state.Phase == "intermission") DrawTurnReveal(state);
        if (state.Phase == "results" && resultsDismissed != state.TurnId) DrawMatchResults(state);
        var rendered = new Vector2(MathF.Round(painting.RenderSize.X), MathF.Round(painting.RenderSize.Y));
        if (rendered != lastCanvasSize && Environment.TickCount64 >= nextLayoutLog)
        {
            log.Write($"ui.canvas size={rendered.X}x{rendered.Y} scale={UiTheme.Scale:F2}");
            lastCanvasSize = rendered; nextLayoutLog = Environment.TickCount64 + 1000;
        }
    }

    private void ChatScreen(RoomView state, Vector2 size)
    {
        using var panel = new UiTheme.PaperPanel("chat-panel", size, compact: true, note: UiTheme.StickyBlue, scrollable: false, plainTop: true, flush: true);
        if (!panel.Success) return;
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
                if (scrollChat) { ImGui.SetScrollHereY(1); scrollChat = false; }
            }
        }
        using (ImRaii.Disabled(client?.Connected != true))
        {
            using var inputColor = ImRaii.PushColor(ImGuiCol.FrameBg, Vector4.Lerp(UiTheme.StickyBlue, UiTheme.Panel, UiTheme.DarkMode ? 0.50f : 0.60f));
            using var inputHover = ImRaii.PushColor(ImGuiCol.FrameBgHovered, Vector4.Lerp(UiTheme.StickyBlue, UiTheme.Panel, 0.75f));
            using var inputActive = ImRaii.PushColor(ImGuiCol.FrameBgActive, Vector4.Lerp(UiTheme.StickyBlue, UiTheme.Panel, 0.85f));
            ImGui.SetNextItemWidth(Math.Max(UiTheme.S(60), ImGui.GetContentRegionAvail().X - UiTheme.S(48)));
            if (focusChat) { ImGui.SetKeyboardFocusHere(); focusChat = false; }
            bool enter = ImGui.InputTextWithHint("##message", state.YouAreDone ? "Say something..." : "Talk or guess...", ref chatInput, 300, ImGuiInputTextFlags.EnterReturnsTrue);
            if (ImGui.IsItemHovered()) UiTheme.Tooltip(state.YouAreDone ? "Done mode: only the drawer and correct guessers see your messages." : "Talk and guess in the same chat.");
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
            clicked = UiTheme.GlyphButton(label, icon.ToIconString(), UiTheme.V(28, 28));
        if (ImGui.IsItemHovered() || ImGui.IsItemFocused()) UiTheme.Tooltip(label);
        return clicked;
    }

    private void OpenProfile()
    {
        if (profile is null) { NotifyStatus("Connect to load your profile first."); return; }
        displayName = profile.DisplayName;
        avatar.Load(AvatarPresets.Pixels(profile));
        avatarPreset = profile.AvatarPreset;
        avatarReset = profile.AvatarPixels is null && avatarPreset is null;
        avatarHistory.Clear();
        profileSettingsPage = 0;
        screen = "profile";
        RequestLeaderboard();
    }

    private void ProfileScreen()
    {
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
                    AvatarChoices();
                    avatarEditor.Toolbar("avatar-tools", transparency: true, undo: () => { avatarHistory.Undo(avatar); avatarReset = false; avatarPreset = null; }, canUndo: avatarHistory.CanUndo);
                    avatarEditor.Draw("avatar-canvas", avatar, new Vector2(ImGui.GetContentRegionAvail().X,
                        Math.Max(UiTheme.S(100), ImGui.GetContentRegionAvail().Y - UiTheme.S(38))), true,
                        operation => { avatarHistory.Capture(avatar, operation.StrokeId); avatar.Apply(operation); avatarReset = false; avatarPreset = null; });
                    UiTheme.Gap(5);
                    if (UiTheme.Button("Use my default avatar", 0, quiet: true) && profile is not null)
                    { avatar.Load(AvatarGenerator.Generate(profile.AvatarSeed)); avatarReset = true; avatarPreset = null; avatarHistory.Clear(); }
                }
            }
            ImGui.TableNextColumn();
            using var card = new UiTheme.PaperPanel("profile-details", Vector2.Zero, note: UiTheme.StickyRose);
            if (!card.Success) return;
            float backWidth = ImGui.CalcTextSize(room is null ? "Back to home" : "Back to room").X + UiTheme.S(36);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, ImGui.GetContentRegionAvail().X - backWidth));
            if (BackButton()) return;
            UiTheme.Gap(4);
            if (UiTheme.Tab("Artist", profileSettingsPage == 0)) profileSettingsPage = 0;
            ImGui.SameLine(); if (UiTheme.Tab("Privacy & invitations", profileSettingsPage == 1)) profileSettingsPage = 1;
            UiTheme.Gap(10);
            if (profileSettingsPage == 1)
            {
                ProfilePrivacyControls();
                UiTheme.Gap(8); UiTheme.Caption("Changes are saved automatically.");
                return;
            }
            UiTheme.Field("Display name", "##profile-name", ref displayName, 40, "Your artist name");
            UiTheme.Wrapped("Character unverified. Your name and avatar are shared with the people in your room.", UiTheme.Muted);
            if (UiTheme.Button("View my sketchbook", 0, quiet: true)) ViewArtistProfile(device!.AccountId);
            UiTheme.Gap(6);
            using (ImRaii.Disabled(savingProfile || client?.Connected != true || !Protocol.ValidName(displayName)))
                if (UiTheme.NoteButton(savingProfile ? "Saving..." : "Save my profile", ImGui.GetContentRegionAvail().X, UiTheme.StickyRose, primary: true))
                {
                    savingProfile = Send(new("profile", Text: displayName, Avatar: avatarReset || avatarPreset is not null ? null : (byte[])avatar.Pixels.Clone(), ResetAvatar: avatarReset, AvatarPreset: avatarPreset));
                    if (savingProfile) NotifyStatus("Saving profile...");
                }
            UiTheme.Rule(); UiTheme.Caption("ACCOUNT RECOVERY");
            UiTheme.Wrapped("Lost your saved code? Replace it while this device still has access. Your previous code stops working and other devices are signed out.", UiTheme.Muted);
            using (ImRaii.Disabled(busy || device is null || client?.Connected != true))
                if (UiTheme.NoteButton("Replace recovery code", ImGui.GetContentRegionAvail().X, UiTheme.StickyRose))
                {
                    string token = device!.AccessToken;
                    Work(async (active, complete) => await AcceptCredentials(active, complete, await active.RotateRecoveryAsync(token)), "Replacing recovery code");
                }
        }
        finally { ImGui.EndTable(); }
    }

    private void Preferences()
    {
        if (PageHeading("Your sketchbook")) return;
        UiTheme.Gap(10);
        using var card = new UiTheme.PaperPanel("sketchbook-preferences", Vector2.Zero);
        if (!card.Success) return;
        bool reduceMotion = plugin.Config.ReduceMotion;
        bool darkMode = plugin.Config.DarkMode;
        bool sounds = plugin.Config.GameSounds;
        if (ImGui.Checkbox("Game sounds", ref sounds)) { plugin.Config.GameSounds = sounds; plugin.Save(); if (!sounds) audio.Stop(); }
        if (ImGui.Checkbox("Dark paper", ref darkMode)) { plugin.Config.DarkMode = darkMode; plugin.Save(); }
        if (ImGui.Checkbox("Reduce decorative motion", ref reduceMotion)) { plugin.Config.ReduceMotion = reduceMotion; plugin.Save(); }
        UiTheme.Caption("Keep the moogle pom still while moving the book.");
        UiTheme.Wrapped("The standard frame is visible only in this debug view. Otherwise, drag empty paper to move the book and use its corner to resize.", UiTheme.Muted);
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

    private void AvatarChoices()
    {
        ImGui.SetNextItemWidth(-1);
        if (!ImGui.BeginCombo("##avatar-presets", avatarPreset is { } id ? AvatarPresets.Find(id)?.Name ?? "Custom drawing" : avatarReset ? "Your default moogle" : "Custom drawing")) return;
        try
        {
            if (ImGui.Selectable("Your default moogle") && profile is not null)
            { avatar.Load(AvatarGenerator.Generate(profile.AvatarSeed)); avatarReset = true; avatarPreset = null; avatarHistory.Clear(); }
            if (ImGui.Selectable("New transparent drawing"))
            { avatar.Clear(); avatarReset = false; avatarPreset = null; avatarHistory.Clear(); }
            foreach (var preset in AvatarPresets.Items)
                if (ImGui.Selectable(preset.Name, avatarPreset == preset.Id))
                { avatar.Load(AvatarGenerator.Generate(preset.Seed)); avatarPreset = preset.Id; avatarReset = false; avatarHistory.Clear(); }
        }
        finally { ImGui.EndCombo(); }
    }

    private static string SafeCommand(string? command) => command is "start" or "join" or "create" or "draw" or "undo" or "vote-kick" or "vote-skip" or "choose-word" or "cursor" or "profile" or "chat" or "sync" or "kick" or "leave" or "list" ? command : "unknown";
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
