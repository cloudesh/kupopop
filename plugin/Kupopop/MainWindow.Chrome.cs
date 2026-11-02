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
        if (!ShowDebug)
        {
            Flags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground;
        }

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
                if (!ConnectionReady)
                {
                    ConnectingScreen();
                }
                else if (newRecovery.Length > 0)
                {
                    RecoveryScreen();
                }
                else if (ShowDebug)
                {
                    Preferences();
                }
                else if (screen == "welcome")
                {
                    Welcome();
                }
                else if (screen == "profile")
                {
                    ProfileScreen();
                }
                else if (screen == "friends")
                {
                    FriendsScreen();
                }
                else if (screen == "leaderboards")
                {
                    LeaderboardsScreen();
                }
                else if (screen == "suggestions")
                {
                    SuggestionsScreen();
                }
                else if (screen == "game" && room is not null)
                {
                    Game();
                }
                else if (screen == "public")
                {
                    PublicRooms();
                }
                else if (screen == "create")
                {
                    CreateRoomScreen();
                }
                else
                {
                    Menu();
                }
            }
        }
        UiChrome.Footer(position, position + ImGui.GetWindowSize(), "v" + UiVersion, IsPinned);
        DrawToasts();
        SharedProfilePopup();
    }

    private void DrawHeader()
    {
        if (!ImGui.BeginTable("brand-paper-controls", 4, ImGuiTableFlags.NoPadOuterX))
        {
            return;
        }

        try
        {
            ImGui.TableSetupColumn("Brand", ImGuiTableColumnFlags.WidthFixed, UiTheme.S(215));
            ImGui.TableSetupColumn("Navigation", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Service status", ImGuiTableColumnFlags.WidthFixed, UiTheme.S(114));
            ImGui.TableSetupColumn("Paper controls", ImGuiTableColumnFlags.WidthFixed, UiTheme.S(ShowDebug ? 32 : 100));
            ImGui.TableNextColumn();
            var face = ImGui.GetCursorScreenPos();
            ImGui.Dummy(UiTheme.V(211, 42));
            var afterBrand = ImGui.GetCursorScreenPos();
            UiStationery.Logo(face + UiTheme.V(20, 31), UiTheme.S(14), pom.Angle);
            UiBrand.Wordmark(face + UiTheme.V(46, 10), UiTheme.V(161, 30));
            ImGui.SetCursorScreenPos(afterBrand);
            ImGui.TableNextColumn();
            UiTheme.Gap(1);
            if (ConnectionReady && newRecovery.Length == 0 && screen != "welcome")
            {
                Navigation();
            }

            ImGui.TableNextColumn();
            bool online = ConnectionReady;
            UiTheme.Gap(1);
            ImGui.AlignTextToFramePadding();
            UiAssets.Glyph(FontAwesomeIcon.Circle, online ? UiTheme.Green : UiTheme.Muted);
            ImGui.SameLine();
            ImGui.TextUnformatted(online ? "Connected" : "Connecting...");
            ImGui.TableNextColumn();
            UiTheme.Gap(1);
            if (ToolAction(plugin.Config.DarkMode ? FontAwesomeIcon.Sun : FontAwesomeIcon.Moon,
                plugin.Config.DarkMode ? "Use light paper" : "Use dark paper"))
            {
                plugin.Config.DarkMode = !plugin.Config.DarkMode;
                plugin.Save();
            }
            if (!ShowDebug)
            {
                ImGui.SameLine();
                if (ToolAction(FontAwesomeIcon.Thumbtack, IsPinned ? "Unpin book" : "Pin book", IsPinned))
                {
                    IsPinned = !IsPinned;
                }

                ImGui.SameLine();
                if (ToolAction(FontAwesomeIcon.Times, "Close book"))
                {
                    IsOpen = false;
                    SynchronizeVisibility();
                }
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
        {
            ShowDebug = false;
            screen = room is null ? "menu" : "game";
        }
        ImGui.SameLine(0, UiTheme.S(2));
        using (ImRaii.Disabled(profile is null))
        {
            if (UiTheme.Tab("Profile", !ShowDebug && screen == "profile", tabPadding))
            {
                ShowDebug = false;
                OpenProfile();
            }
        }

        ImGui.SameLine(0, UiTheme.S(2));
        using (ImRaii.Disabled(profile is null))
        {
            if (UiTheme.Tab("Friends", !ShowDebug && screen == "friends", tabPadding))
            {
                OpenFriends();
            }
        }

        ImGui.SameLine(0, UiTheme.S(2));
        using (ImRaii.Disabled(profile is null))
        {
            if (UiTheme.Tab("Leaderboards", !ShowDebug && screen == "leaderboards", tabPadding))
            {
                ShowDebug = false;
                screen = "leaderboards";
                RequestLeaderboard();
            }
        }
    }

    private bool PageHeading(string title, bool refresh = false)
    {
        bool back = false;
        if (ImGui.BeginTable("page-heading", 2, ImGuiTableFlags.NoPadOuterX))
        {
            try
            {
                float actions = ImGui.CalcTextSize(room is null ? "Back to home" : "Back to room").X + UiTheme.S(36);
                if (refresh)
                {
                    actions += ImGui.CalcTextSize("Refresh").X + UiTheme.S(24);
                }

                ImGui.TableSetupColumn("Title", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, actions);
                ImGui.TableNextColumn();
                art.Heading(title);
                ImGui.TableNextColumn();
                back = BackButton();
                if (refresh)
                {
                    ImGui.SameLine();
                    if (UiTheme.Button("Refresh", 0, quiet: true))
                    {
                        Send(new("list"));
                    }
                }
            }
            finally { ImGui.EndTable(); }
        }
        UiTheme.Gap(6);
        return back;
    }

    private bool BackButton()
    {
        if (!UiTheme.BackButton(room is null ? "Back to home" : "Back to room"))
        {
            return false;
        }

        ShowDebug = false;
        screen = room is null ? "menu" : "game";
        return true;
    }

    private static bool ToolAction(FontAwesomeIcon icon, string label, bool selected = false)
    {
        bool clicked;
        using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
        using (ImRaii.PushColor(ImGuiCol.Button, selected ? UiTheme.RoseTint : Vector4.Zero))
        using (ImRaii.PushColor(ImGuiCol.Text, selected ? UiTheme.Rose : UiTheme.Ink))
        {
            clicked = UiTheme.GlyphButton(label, icon.ToIconString(), UiTheme.V(28, 28));
        }

        if (ImGui.IsItemHovered())
        {
            UiTheme.Tooltip(label);
        }

        return clicked;
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
