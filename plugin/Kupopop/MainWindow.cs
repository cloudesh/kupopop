using System.Collections.Concurrent;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Kupopop.Shared;

namespace Kupopop;

public sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly DiagnosticLog log;
    private readonly CredentialVault vault;
    private readonly ConcurrentQueue<Action> actions = new();
    private readonly CanvasWidget painting = new();
    private readonly CanvasWidget avatarEditor = new();
    private readonly PixelCanvas canvas = new();
    private readonly PixelCanvas avatar = new(64, 64);
    private readonly AvatarTextures avatars = new();
    private readonly List<ChatLine> chat = [];
    private readonly HashSet<Guid> muted = [];
    private RemoteClient? client;
    private DeviceAccount? device;
    private PlayerProfile? profile;
    private RoomView? room;
    private RoomSummary[] publicRooms = [];
    private Uri? endpoint;
    private string serverUrl;
    private string displayName = "";
    private string recoveryInput = "";
    private string newRecovery = "";
    private string joinCode = "";
    private string roomTitle = "A kupo drawing room";
    private string chatInput = "";
    private string screen = "welcome";
    private string status = "";
    private bool busy, disposed, publicRoom, savedRecovery, revealRecovery, avatarReset, scrollChat;
    private bool reconnect;
    private int seconds = 90, rounds = 2, hints = 30, capacity = 16, retry;
    private long reconnectAt, heartbeatAt, statsAt, lastSync;
    private int received, draws;
    private TimeSpan serverOffset;
    public bool ShowDebug;

    public MainWindow(Plugin plugin, string directory, DiagnosticLog log)
        : base("Kupopop Draw###Kupopop", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;
        this.log = log;
        vault = new(directory);
        serverUrl = plugin.Config.ServerUrl;
        Size = new(1180, 760);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new() { MinimumSize = new(940, 600), MaximumSize = new(float.MaxValue) };
        ConfigureClient();
    }

    private void ConfigureClient()
    {
        client?.Dispose();
        client = null; device = null; profile = null; room = null; reconnect = false;
        newRecovery = "";
        chat.Clear();
        try
        {
            endpoint = new Uri(serverUrl.Trim().TrimEnd('/') + "/");
            client = new(endpoint, log.Write);
            device = vault.Load(endpoint);
            screen = device is null ? "welcome" : "menu";
            status = device is null ? "" : "Account found on this device. Connect when ready.";
            plugin.Config.ServerUrl = endpoint.GetLeftPart(UriPartial.Authority);
            serverUrl = plugin.Config.ServerUrl;
            plugin.Save();
            log.Write("client.configured device_found=" + (device is not null));
        }
        catch (Exception ex)
        {
            log.Write("client.configuration_failed", ex);
            status = "Check the HTTPS hostname. If a device credential cannot be opened, use account recovery.";
            screen = "welcome";
        }
    }

    public void PollNetwork()
    {
        while (actions.TryDequeue(out var action)) if (!disposed) action();
        int budget = 256;
        while (budget-- > 0 && client?.TryRead(out var message) == true)
        {
            if (message is null) continue;
            received++;
            switch (message.Type)
            {
                case "profile":
                    profile = message.Profile;
                    if (profile is not null && screen != "profile") displayName = profile.DisplayName;
                    break;
                case "state" when message.Room is { } state:
                    if (state.Players.Length > 16) throw new InvalidDataException("Server player limit exceeded.");
                    bool differentRoom = room?.Code != state.Code;
                    bool newTurn = room?.TurnId != state.TurnId;
                    if (differentRoom) chat.Clear();
                    if (state.ResetCanvas || newTurn) canvas.Clear();
                    if (state.Canvas is { } pixels) canvas.Load(pixels);
                    room = state;
                    serverOffset = state.ServerTime - DateTimeOffset.UtcNow;
                    if (screen != "profile") screen = "game";
                    log.Write($"room.state phase={state.Phase} round={state.Round} players={state.Players.Length} canvas_reset={state.ResetCanvas}");
                    break;
                case "draw" when message.Draw is { } op:
                    if (room?.TurnId == message.TurnId && message.ActorId != device?.AccountId) canvas.Apply(op);
                    break;
                case "rooms": publicRooms = message.Rooms ?? []; status = $"{publicRooms.Length} public room(s)."; break;
                case "chat" when message.Chat is { } line:
                    if (line.Sender is null || !muted.Contains(line.Sender.Value))
                    { chat.Add(line); if (chat.Count > 200) chat.RemoveAt(0); scrollChat = true; }
                    break;
                case "left": room = null; chat.Clear(); screen = "menu"; status = message.Error ?? "You left the room."; break;
                case "error":
                    status = message.Error ?? "Server rejected the action.";
                    log.Write("server.action_rejected");
                    if (room is not null && message.RejectedCommand == "draw" && Environment.TickCount64 - lastSync > 1000)
                    { lastSync = Environment.TickCount64; Send(new("sync")); }
                    break;
                case "disconnected":
                    status = message.Error ?? "Disconnected. Trying to reconnect.";
                    if (reconnect) reconnectAt = Environment.TickCount64 + 3000;
                    break;
            }
        }
        long now = Environment.TickCount64;
        if (client?.Connected == true)
        {
            if (now - heartbeatAt > 15000) { Send(new("ping")); heartbeatAt = now; }
        }
        else if (reconnect && !busy && device is not null && now >= reconnectAt && retry < 8) Connect();
        if (now - statsAt > 10000)
        {
            log.Write($"client.stats connected={client?.Connected == true} events={received} drawings_sent={draws} room_phase={room?.Phase ?? "none"}");
            statsAt = now;
        }
    }

    public override void Draw()
    {
        ImGui.TextColored(new Vector4(0.91f, 0.64f, 0.78f, 1), "K U P O P O P   /   D R A W");
        ImGui.SameLine();
        ImGui.TextDisabled("experimental 0.1.0");
        ImGui.Separator();
        if (!string.IsNullOrEmpty(status)) Wrapped(status);
        if (ShowDebug || screen == "welcome") ServerSettings();
        if (newRecovery.Length > 0) { RecoveryScreen(); return; }
        if (screen == "welcome") { Welcome(); return; }
        Navigation();
        if (screen == "profile") { ProfileScreen(); return; }
        if (screen == "game" && room is not null) { Game(); return; }
        if (screen == "public") { PublicRooms(); return; }
        Menu();
    }

    private void ServerSettings()
    {
        Wrapped("Server connection: use a hosted HTTPS hostname, or https://localhost:7443 with the local launcher and trusted development certificate.");
        ImGui.SetNextItemWidth(440);
        using (ImRaii.Disabled(busy))
        {
            ImGui.InputText("Server URL", ref serverUrl, 256);
            if (ImGui.Button("Apply server address")) ConfigureClient();
        }
        if (ShowDebug)
        {
            ImGui.TextUnformatted($"Network: {(client?.Connected == true ? "connected" : "disconnected")} | Received: {received} | Drawing messages: {draws}");
            Wrapped(log.FilePath);
            if (ImGui.Button("Copy log path")) ImGui.SetClipboardText(log.FilePath);
            ImGui.SameLine();
            if (ImGui.Button("Resync room")) Send(new("sync"));
            Wrapped("Logs contain connection/state diagnostics and stack traces, without account secrets, answers, chat text, or drawing contents.");
        }
        ImGui.Separator();
    }

    private void Welcome()
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("Welcome, artist!");
        Wrapped("Make something wonderful, guess something ridiculous, and enjoy a little time together in Eorzea.");
        ImGui.Spacing();
        Wrapped("Create a Kupopop account for this server. Your display name is unverified. Only the name and avatar you choose are shared with players in rooms you join. No FFXIV account identifiers are collected.");
        using (ImRaii.Disabled(busy || client is null))
        {
            ImGui.InputText("Display / character name (unverified)", ref displayName, 40);
            using (ImRaii.Disabled(!Protocol.ValidName(displayName)))
                if (ImGui.Button("Create my account")) CreateAccount(false);
            ImGui.Spacing(); ImGui.Separator();
            ImGui.TextUnformatted("Already have an account on this server?");
            ImGui.InputText("Private recovery code", ref recoveryInput, 128, ImGuiInputTextFlags.Password);
            if (ImGui.Button("Recover account")) CreateAccount(true);
        }
        if (busy) ImGui.TextUnformatted("Connecting... please wait.");
    }

    private void RecoveryScreen()
    {
        ImGui.TextUnformatted("Your account is ready. Save your recovery code.");
        Wrapped("Keep this code in a password manager. Anyone with it can recover your account. It is not saved in the plugin configuration or logs. Recovery replaces old credentials and creates a new code.");
        ImGui.Checkbox("Show private code", ref revealRecovery);
        ImGui.SetNextItemWidth(520);
        ImGui.InputText("Recovery code", ref newRecovery, 128,
            ImGuiInputTextFlags.ReadOnly | (revealRecovery ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password));
        if (ImGui.Button("Copy recovery code")) ImGui.SetClipboardText(newRecovery);
        ImGui.Checkbox("I have saved my recovery code somewhere private", ref savedRecovery);
        if (profile is not null) ImGui.Image(avatars.Get(profile).Handle, new Vector2(100));
        using (ImRaii.Disabled(!savedRecovery))
        {
            if (ImGui.Button("Use default avatar and continue")) { newRecovery = ""; screen = room is null ? "menu" : "game"; }
            ImGui.SameLine();
            if (ImGui.Button("Draw my own avatar")) { newRecovery = ""; OpenProfile(); }
        }
    }

    private void Navigation()
    {
        using (ImRaii.Disabled(busy))
        {
            if (client?.Connected != true)
            { if (ImGui.Button("Connect")) Connect(); ImGui.SameLine(); }
            if (ImGui.Button(room is null ? "Game menu" : "Back to room")) screen = room is null ? "menu" : "game";
            ImGui.SameLine();
            if (ImGui.Button("Profile")) OpenProfile();
            ImGui.SameLine();
            if (ImGui.Button("Diagnostics / server")) ShowDebug = !ShowDebug;
            ImGui.SameLine();
            if (ImGui.Button("Disconnect"))
            {
                reconnect = false;
                Work(async () => { if (client is not null) await client.DisconnectAsync(); }, "Disconnecting");
            }
        }
        if (profile is not null) ImGui.TextUnformatted($"{profile.DisplayName}  |  Character unverified");
        ImGui.Separator();
    }

    private void Menu()
    {
        ImGui.TextUnformatted("A little imagination goes a long way.");
        ImGui.Spacing();
        using (ImRaii.Disabled(client?.Connected != true))
        {
            ImGui.InputText("Room code", ref joinCode, 12);
            if (ImGui.Button("Join Room", new Vector2(180, 36))) Send(new("join", Text: joinCode));
            ImGui.SameLine();
            if (ImGui.Button("Public Rooms", new Vector2(180, 36))) { screen = "public"; Send(new("list")); }
            ImGui.Separator();
            ImGui.TextUnformatted("Make Room");
            ImGui.InputText("Room title", ref roomTitle, 40);
            ImGui.Checkbox("List this room publicly", ref publicRoom);
            if (publicRoom) Wrapped("Anyone on this server can see the room title and player count. Your name and avatar become visible to people who join. Avoid including personal information in the room title.");
            ImGui.SetNextItemWidth(160); ImGui.InputInt("Seconds per drawer (15–300)", ref seconds);
            ImGui.SetNextItemWidth(160); ImGui.InputInt("Rounds (1–5)", ref rounds);
            ImGui.SetNextItemWidth(160); ImGui.InputInt("Hint interval seconds (0 = off)", ref hints);
            ImGui.SetNextItemWidth(160); ImGui.InputInt("Players (2–16)", ref capacity);
            if (ImGui.Button("Make Room", new Vector2(180, 36)))
                Send(new("create", Options: new(roomTitle, publicRoom, Math.Clamp(seconds, 15, 300),
                    Math.Clamp(rounds, 1, 5), Math.Clamp(hints, 0, 300), Math.Clamp(capacity, 2, 16))));
        }
    }

    private void PublicRooms()
    {
        ImGui.TextUnformatted("Public Rooms");
        if (ImGui.Button("Refresh")) Send(new("list"));
        using var child = ImRaii.Child("room-list", Vector2.Zero, true);
        if (!child.Success) return;
        if (publicRooms.Length == 0) Wrapped("No public rooms yet. Make a room and choose to list it publicly.");
        foreach (var listed in publicRooms)
        {
            ImGui.PushID(listed.Code);
            ImGui.TextUnformatted($"{listed.Title}   {listed.Players}/{listed.MaxPlayers}   {listed.Phase}");
            using (ImRaii.Disabled(listed.Phase is not ("waiting" or "results") || listed.Players >= listed.MaxPlayers))
                if (ImGui.Button("Join")) Send(new("join", Text: listed.Code));
            ImGui.Separator(); ImGui.PopID();
        }
    }

    private void Game()
    {
        var state = room!;
        int remaining = Math.Max(0, (int)Math.Ceiling((state.Deadline - (DateTimeOffset.UtcNow + serverOffset)).TotalSeconds));
        string answer = state.Word ?? string.Join(' ', state.Hint.ToCharArray());
        ImGui.TextUnformatted($"Round {state.Round}/{state.Options.Rounds}   |   {state.Phase}   |   {(state.Phase is "drawing" or "intermission" ? remaining : 0)}s   |   {answer}");
        ImGui.TextUnformatted($"{state.Options.Title}   Room: {state.Code}");
        ImGui.SameLine(); if (ImGui.SmallButton("Copy code")) ImGui.SetClipboardText(state.Code);
        ImGui.SameLine(); if (ImGui.SmallButton("Leave room")) { Send(new("leave")); return; }
        if (state.HostId == device?.AccountId && state.Phase is "waiting" or "results")
            if (ImGui.Button(state.Phase == "results" ? "Play another match" : "Start match")) Send(new("start"));
        if (state.Phase == "waiting") Wrapped("Waiting for the host to start. At least two players are needed.");
        if (!ImGui.BeginTable("play-layout", 3, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV)) return;
        try
        {
        ImGui.TableSetupColumn("Players", ImGuiTableColumnFlags.WidthFixed, 180);
        ImGui.TableSetupColumn("Drawing", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Chat", ImGuiTableColumnFlags.WidthFixed, 280);
        ImGui.TableNextColumn();
        using (var people = ImRaii.Child("players", Vector2.Zero, false))
        {
            if (people.Success)
                foreach (var player in state.Players.OrderByDescending(p => p.Score))
                {
                    var avatarTexture = avatars.Get(player.Profile);
                    ImGui.PushID(player.Profile.Id.ToString());
                    ImGui.Image(avatarTexture.Handle, new Vector2(36));
                    ImGui.SameLine(); ImGui.TextUnformatted(player.Profile.DisplayName);
                    ImGui.TextUnformatted($"{player.Score} pts  {(player.IsDrawer ? "Drawing" : player.Solved ? "Solved" : "")}{(player.IsHost ? " Host" : "")}{(!player.Connected ? " Offline" : "")}");
                    if (player.Profile.Id != device?.AccountId)
                    {
                        if (ImGui.SmallButton(muted.Contains(player.Profile.Id) ? "Unmute" : "Mute"))
                        { if (!muted.Add(player.Profile.Id)) muted.Remove(player.Profile.Id); }
                        if (state.HostId == device?.AccountId)
                        { ImGui.SameLine(); if (ImGui.SmallButton("Kick")) Send(new("kick", Text: player.Profile.Id.ToString())); }
                    }
                    ImGui.Separator(); ImGui.PopID();
                }
        }
        ImGui.TableNextColumn();
        using (var drawing = ImRaii.Child("drawing", Vector2.Zero, false))
        {
            if (drawing.Success)
            {
                bool canDraw = client?.Connected == true && state.Phase == "drawing" && state.DrawerId == device?.AccountId;
                ImGui.TextUnformatted(canDraw ? "Your turn to draw!" : "Watch the canvas and guess in chat.");
                using (ImRaii.Disabled(!canDraw)) painting.Toolbar("game-tools");
                painting.Draw("game-canvas", canvas, ImGui.GetContentRegionAvail(), canDraw, operation =>
                {
                    if (Send(new("draw", Draw: operation, TurnId: state.TurnId))) { canvas.Apply(operation); draws++; }
                });
            }
        }
        ImGui.TableNextColumn();
        ChatScreen(state);
        }
        finally { ImGui.EndTable(); }
    }

    private void ChatScreen(RoomView state)
    {
        Wrapped(state.YouAreDone ? "DONE MODE: drawer and correct guessers only" : "Talk and guess here");
        using (var messages = ImRaii.Child("messages", new Vector2(0, -40), true))
        {
            if (messages.Success)
            {
                foreach (var line in chat)
                {
                    if (line.Done) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.69f, 0.80f, 1, 1));
                    Wrapped($"{(line.Done ? "[done] " : "")}{line.Name}: {line.Text}");
                    if (line.Done) ImGui.PopStyleColor();
                }
                if (scrollChat) { ImGui.SetScrollHereY(1); scrollChat = false; }
            }
        }
        using (ImRaii.Disabled(client?.Connected != true))
        {
            ImGui.SetNextItemWidth(Math.Max(60, ImGui.GetContentRegionAvail().X - 55));
            bool enter = ImGui.InputText("##message", ref chatInput, 300, ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.SameLine();
            bool click = ImGui.Button("Send");
            if ((enter || click) && !string.IsNullOrWhiteSpace(chatInput))
            { Send(new("chat", Text: chatInput, TurnId: state.TurnId)); chatInput = ""; if (enter) ImGui.SetKeyboardFocusHere(-1); }
        }
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
        ImGui.TextUnformatted("Your Kupopop profile");
        Wrapped("Character association: unverified. Drawing a custom avatar is optional. This profile is designed for future Kupopop games; currency is not enabled yet.");
        ImGui.InputText("Display name", ref displayName, 40);
        avatarEditor.Toolbar("avatar-tools");
        avatarEditor.Draw("avatar-canvas", avatar, new Vector2(300, 300), true, op => { avatar.Apply(op); avatarReset = false; });
        if (ImGui.Button("Use my default avatar") && profile is not null)
        { avatar.Load(AvatarGenerator.Generate(profile.AvatarSeed)); avatarReset = true; }
        using (ImRaii.Disabled(client?.Connected != true || !Protocol.ValidName(displayName)))
            if (ImGui.Button("Save profile"))
            {
                Send(new("profile", Text: displayName, Avatar: avatarReset ? null : (byte[])avatar.Pixels.Clone(), ResetAvatar: avatarReset));
                screen = room is null ? "menu" : "game";
            }
        ImGui.Spacing();
        Wrapped("If you lose your saved recovery code, you can replace it while this device still has access. Replacement invalidates your previous recovery code and signs other devices out.");
        using (ImRaii.Disabled(busy || device is null || client?.Connected != true))
            if (ImGui.Button("Replace recovery code"))
            {
                var activeClient = client!;
                string token = device!.AccessToken;
                Work(async () => await AcceptCredentials(await activeClient.RotateRecoveryAsync(token)), "Replacing recovery code");
            }
    }

    private void CreateAccount(bool recovering)
    {
        if (client is null) return;
        var activeClient = client;
        string name = displayName, code = recoveryInput;
        Work(async () =>
        {
            var result = recovering ? await activeClient.RecoverAsync(code) : await activeClient.CreateAsync(name);
            await AcceptCredentials(result);
        }, recovering ? "Recovering account" : "Creating account");
    }

    private async Task AcceptCredentials(AccountCredentials result)
    {
        var saved = new DeviceAccount(result.AccountId, result.AccessToken);
        vault.Save(endpoint!, saved);
        actions.Enqueue(() =>
        {
            device = saved; profile = result.Profile; displayName = result.Profile.DisplayName;
            newRecovery = result.RecoveryCode; recoveryInput = ""; savedRecovery = false; revealRecovery = false;
            retry = 0; reconnect = true;
        });
        await client!.ConnectAsync(result.AccessToken);
        actions.Enqueue(() => status = "Account ready. Save the recovery code below.");
    }

    private void Connect()
    {
        if (client is null || device is null || busy) return;
        reconnect = true;
        var activeClient = client;
        string token = device.AccessToken;
        Work(async () =>
        {
            await activeClient.ConnectAsync(token);
            actions.Enqueue(() => { retry = 0; status = "Connected."; });
        }, "Connecting");
    }

    private bool Send(ClientCommand command)
    {
        bool sent = client?.Send(command) == true;
        if (!sent) status = "Not connected. Open Diagnostics / server and reconnect.";
        return sent;
    }

    private static void Wrapped(string text)
    {
        ImGui.PushTextWrapPos(0);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }

    private void Work(Func<Task> operation, string label)
    {
        if (busy) return;
        busy = true; status = label + "...";
        _ = Task.Run(async () =>
        {
            try { await operation().ConfigureAwait(false); }
            catch (Exception ex)
            {
                log.Write("client.operation_failed category=" + label, ex);
                actions.Enqueue(() =>
                {
                    status = $"{label} failed ({ex.GetType().Name}). Check the server, certificate, and diagnostic log.";
                    retry++; reconnectAt = Environment.TickCount64 + Math.Min(30000, 2000 * (1 << Math.Min(retry, 4)));
                });
            }
            finally { actions.Enqueue(() => busy = false); }
        });
    }

    public void Dispose()
    {
        disposed = true; reconnect = false;
        client?.Dispose();
        painting.Dispose(); avatarEditor.Dispose(); avatars.Dispose();
        newRecovery = ""; recoveryInput = "";
    }
}
