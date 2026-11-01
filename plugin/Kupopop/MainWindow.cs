using System.Collections.Concurrent;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Kupopop.Shared;

namespace Kupopop;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly DiagnosticLog log;
    private readonly CredentialVault vault;
    private readonly ConcurrentQueue<(RemoteClient Owner, Action Apply)> actions = new();
    private readonly ConcurrentQueue<(RemoteClient Owner, AccountCredentials Credentials)> pendingCredentials = new();
    private readonly CanvasWidget painting = new();
    private readonly CanvasWidget avatarEditor = new();
    private readonly PixelCanvas canvas = new();
    private readonly PixelCanvas avatar = new(64, 64, transparency: true);
    private readonly CanvasHistory avatarHistory = new();
    private string? avatarPreset;
    private WordListInfo[] wordLists = [];
    private readonly HashSet<string> selectedWordLists = [];
    private bool wordListsChosen, undoPending, canUndoDrawing;
    private readonly ToastQueue toasts = new();
    private readonly UpdateChecker updates = new();
    private readonly GameAudio audio;
    private bool audioStateReady;
    private bool updateCheckPending;
    private long nextUpdateCheck, remoteCursorAt;
    private readonly AvatarTextures avatars;
    private readonly UiAssets art;
    private readonly List<ChatLine> chat = [];
    private RemoteClient? client;
    private Task sessionClosing = Task.CompletedTask;
    private DeviceAccount? device;
    private PlayerProfile? profile;
    private RoomView? room;
    private RoomSummary[] publicRooms = [];
    private Uri? endpoint;
    private string displayName = "";

    private string recoveryInput = "";
    private string newRecovery = "";
    private string joinCode = "";
    private string roomTitle = "A kupo drawing room";
    private string chatInput = "";
    private string screen = "welcome";
    private bool busy, disposed, publicRoom, savedRecovery, revealRecovery, avatarReset, scrollChat, savingProfile;
    private bool serviceAvailable, sessionReady;
    private readonly ConnectionRetry connectionRetry = new();
    private string connectionStatus = "Finding the drawing room...";
    private long sessionDeadline;
    private int seconds = 90, rounds = 2, hints = 30, capacity = 16;
    private int wordChoiceCount = 6;
    private int roomSetupPage;
    private string customWordsText = "", roomCustomWords = "";
    private bool voteKickEnabled = true, voteSkipEnabled = true, customWordsEnabled = true;
    private readonly HashSet<string> collapsedWordCategories = [];
    private string customWordInput = "";
    private Guid customWordTurn, resultsDismissed;
    private bool enteringCustomWord, savingRoomSettings;
    private long heartbeatAt, statsAt, lastSync;
    private int received, draws;
    private TimeSpan serverOffset;
    public bool ShowDebug;

    public MainWindow(Plugin plugin, string directory, DiagnosticLog log)
        : base("Kupo Skribbl###KupopopPaper", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;
        this.log = log;
        audio = new(log);
        avatars = new();
        art = new(log);
        vault = new(directory);
        Size = UiWindowGeometry.DefaultSize;
        SizeCondition = ImGuiCond.FirstUseEver;
        IsOpen = false;
    }

    public override void OnOpen() => SynchronizeVisibility();
    public override void OnClose() => CloseSession();

    public void SynchronizeVisibility()
    {
        if (disposed || !IsOpen) { CloseSession(); return; }
        if (client is null && sessionClosing.IsCompleted) ConfigureClient();
    }

    private void CloseSession()
    {
        var closing = client;
        client = null;
        audio.Stop();
        audioStateReady = false;
        room = null; profile = null; device = null;
        chat.Clear(); publicRooms = [];
        social = null; sharedProfile = null; showSharedProfile = false;
        leaderboard = null;
        customWordsText = roomCustomWords = "";
        suggestionText = "";
        serviceAvailable = sessionReady = busy = savingProfile = savingRoomSettings = false;
        painting.RemoteCursor = null;
        undoPending = canUndoDrawing = false;
        updateCheckPending = false;
        if (closing is not null)
        {
            // CloseAsync stops HTTP work immediately and sends a normal WebSocket
            // close so the server removes membership without reconnect grace.
            sessionClosing = closing.CloseAsync();
            log.Write("client.window_closed");
        }
    }

    private void ConfigureClient()
    {
        client?.Dispose();
        client = null; device = null; profile = null; room = null;
        serviceAvailable = sessionReady = false;
        busy = false;
        connectionRetry.Connected();
        heartbeatAt = 0;
        chat.Clear();
        try
        {
            endpoint = new Uri(OfficialService.Origin.TrimEnd('/') + "/");
            client = new(endpoint, log.Write);
            try { device = vault.Load(endpoint); }
            catch (Exception ex)
            {
                log.Write("client.credential_unavailable", ex);
                NotifyStatus("This device could not open its saved sign-in. Use your private recovery code to recover your profile.", error: true);
                returningArtist = true;
            }
            screen = device is null ? "welcome" : "menu";
            log.Write("client.configured device_found=" + (device is not null));
        }
        catch (Exception ex)
        {
            log.Write("client.configuration_failed", ex);
            connectionStatus = "The game service is not configured. Please install the latest plugin update.";
            screen = "welcome";
        }
    }

    public void PollNetwork()
    {
        SynchronizeVisibility();
        if (disposed || !IsOpen) return;
        audio.Tick();
        while (pendingCredentials.TryDequeue(out var pending))
        {
            // A completed recovery must not lose its new code when the user
            // closes the window while the HTTP response is arriving.
            if (!ReferenceEquals(pending.Owner, client)) ConfigureClient();
            var result = pending.Credentials;
            device = new(result.AccountId, result.AccessToken);
            profile = result.Profile; displayName = result.Profile.DisplayName;
            newRecovery = result.RecoveryCode; recoveryInput = ""; savedRecovery = revealRecovery = false;
            sessionDeadline = Environment.TickCount64 + 15000;
        }
        while (actions.TryDequeue(out var action))
            if (ReferenceEquals(action.Owner, client)) action.Apply();
        int budget = 256;
        while (budget-- > 0 && client?.TryRead(out var message) == true)
        {
            if (message is null) continue;
            HandleEvent(message);
        }
        long now = Environment.TickCount64;
        if (now - remoteCursorAt > 2000) painting.RemoteCursor = null;
        if (!updateCheckPending && now >= nextUpdateCheck)
        {
            var owner = client!;
            updateCheckPending = true; nextUpdateCheck = now + 600000;
            _ = Task.Run(async () =>
            {
                try
                {
                    var newer = await updates.CheckAsync(typeof(Plugin).Assembly.GetName().Version ?? new Version(0, 0));
                    if (newer is not null) actions.Enqueue((owner, () =>
                    {
                        if (plugin.Config.DismissedUpdate != newer.ToString())
                            toasts.Show("update:" + newer, "An update is available", $"Kupo Skribbl {(newer.Build >= 0 ? newer.ToString(3) : newer.ToString())} is ready. Update through the Dalamud plugin installer.");
                    }
                        ));
                }
                catch (Exception ex) { if (!disposed) log.Write("update.check_failed", ex); }
                finally { actions.Enqueue((owner, () => updateCheckPending = false)); }
            });
        }
        if (client?.Connected == true)
        {
            if (!sessionReady && !busy && now >= sessionDeadline)
            {
                connectionStatus = "The server took too long to load your profile. Trying again...";
                Work(async (active, _) => await active.DisconnectAsync(), "Reconnecting");
            }
            if (now - heartbeatAt > 15000) { Send(new("ping")); heartbeatAt = now; }
        }
        else if (client is not null && !busy && connectionRetry.Due(now) && (!serviceAvailable || device is not null)) Connect();
        else if (device is null && serviceAvailable && !busy && now >= heartbeatAt)
        { Connect(); }
        if (now - statsAt > 10000)
        {
            log.Write($"client.stats connected={client?.Connected == true} events={received} drawings_sent={draws} room_phase={room?.Phase ?? "none"}");
            statsAt = now;
        }
    }

    private void HandleEvent(ServerEvent message)
    {
        received++;
        if (HandleSocialEvent(message)) return;
        switch (message.Type)
        {
            case "profile":
                profile = message.Profile;
                if (profile is not null) { sessionReady = serviceAvailable = true; connectionRetry.Connected(); }
                if (savingProfile)
                { savingProfile = false; screen = room is null ? "menu" : "game"; NotifyStatus("Profile saved."); }
                if (profile is not null && screen != "profile") displayName = profile.DisplayName;
                break;
            case "state" when message.Room is { } state:
                if (state.Players.Length > 16) throw new InvalidDataException("Server player limit exceeded.");
                bool differentRoom = room?.Code != state.Code;
                bool newTurn = room?.TurnId != state.TurnId;
                if (differentRoom) audio.Stop();
                if (differentRoom) chat.Clear();
                if (state.ResetCanvas || newTurn) canvas.Clear();
                if (state.Canvas is { } pixels) canvas.Load(pixels);
                if (newTurn || state.Canvas is not null) { undoPending = false; canUndoDrawing = state.CanUndo; }
                if (newTurn || state.Phase != "drawing") painting.RemoteCursor = null;
                if (state.Cursor is { } brush && state.DrawerId != device?.AccountId)
                { painting.RemoteCursor = brush; remoteCursorAt = Environment.TickCount64; }
                if (audioStateReady && plugin.Config.GameSounds)
                    foreach (var cue in GameSoundEvents.Transition(room, state, device?.AccountId)) audio.Enqueue(cue);
                audioStateReady = true;
                room = state;
                if (differentRoom) Send(new("social"));
                serverOffset = state.ServerTime - DateTimeOffset.UtcNow;
                if (screen is not ("profile" or "friends") && !(screen == "create" && !differentRoom && state.Phase is "waiting" or "results")) screen = "game";
                if (differentRoom && inviteAfterCreate && state.HostId == device?.AccountId)
                { inviteAfterCreate = false; OpenFriends(inviting: true); }
                log.Write($"room.state phase={state.Phase} round={state.Round} players={state.Players.Length} canvas_reset={state.ResetCanvas}");
                break;
            case "draw" when message.Draw is { } op:
                if (room?.TurnId == message.TurnId && message.ActorId != device?.AccountId) canvas.Apply(op);
                break;
            case "canvas" when message.Canvas is { } restored && room?.TurnId == message.TurnId:
                canvas.Load(restored); undoPending = false; canUndoDrawing = message.CanUndo;
                break;
            case "cursor" when message.Cursor is { } pointer && room?.TurnId == message.TurnId && message.ActorId == room.DrawerId && message.ActorId != device?.AccountId:
                painting.RemoteCursor = pointer; remoteCursorAt = Environment.TickCount64;
                break;
            case "wordlists":
                wordLists = message.WordLists ?? [];
                if (!wordListsChosen)
                {
                    selectedWordLists.Clear();
                    foreach (var list in wordLists.Where(l => l.DefaultEnabled)) selectedWordLists.Add(list.Id);
                }
                break;
            case "rooms": publicRooms = message.Rooms ?? []; NotifyStatus($"{publicRooms.Length} public room(s)."); break;
            case "room-configured":
                savingRoomSettings = false; screen = "game"; NotifyStatus("Room settings saved.");
                break;
            case "chat" when message.Chat is { } line:
                chat.Add(line); if (chat.Count > 200) chat.RemoveAt(0); scrollChat = true;
                break;
            case "left": audio.Stop(); room = null; chat.Clear(); screen = "menu"; NotifyStatus(message.Error ?? "You left the room."); break;
            case "error":
                NotifyStatus(message.Error ?? "Server rejected the action.", error: true);
                if (message.RejectedCommand == "profile") savingProfile = false;
                if (message.RejectedCommand == "configure") savingRoomSettings = false;
                if (message.RejectedCommand == "undo") { undoPending = false; canUndoDrawing = false; Send(new("sync")); }
                log.Write("server.action_rejected action=" + SafeCommand(message.RejectedCommand) + " reason=" + SafeReason(message.Error));
                if (room is not null && message.RejectedCommand == "draw" && Environment.TickCount64 - lastSync > 1000)
                { lastSync = Environment.TickCount64; Send(new("sync")); }
                break;
            case "disconnected":
                audio.Stop();
                audioStateReady = false;
                savingProfile = false;
                undoPending = false;
                painting.RemoteCursor = null;
                sessionReady = serviceAvailable = false;
                connectionStatus = "Connection interrupted. Rejoining your drawing room automatically...";
                if (connectionRetry.Due(Environment.TickCount64)) connectionRetry.Failed(Environment.TickCount64);
                break;
        }

    }

    private void CreateAccount(bool recovering, bool enableAll = false)
    {
        if (client is null) return;
        string name = displayName, code = recoveryInput;
        bool share = enableAll;
        Work(async (activeClient, complete) =>
        {
            var result = recovering ? await activeClient.RecoverAsync(code) : await activeClient.CreateAsync(name, share, enableAll, enableAll);
            await AcceptCredentials(activeClient, complete, result);
        }, recovering ? "Recovering account" : "Creating account");
    }

    private async Task AcceptCredentials(RemoteClient activeClient, Action<Action> complete, AccountCredentials result)
    {
        var saved = new DeviceAccount(result.AccountId, result.AccessToken);
        vault.Save(endpoint!, saved);
        pendingCredentials.Enqueue((activeClient, result));
        await activeClient.ConnectAsync(result.AccessToken);
        complete(() => NotifyStatus("Account ready. Save the recovery code below."));
    }

    private void Connect()
    {
        if (client is null || busy) return;
        // A fresh authenticated snapshot replaces potentially expired room state.
        // Room membership is restored by the server when its reconnect grace applies.
        if (device is not null) { room = null; chat.Clear(); if (screen == "game") screen = "menu"; }
        sessionReady = false;
        connectionStatus = device is null ? "Checking the game service..." : "Signing in to your Kupopop profile...";
        string? token = device?.AccessToken;
        Work(async (activeClient, complete) =>
        {
            await activeClient.CheckHealthAsync();
            if (token is not null) await activeClient.ConnectAsync(token);
            complete(() =>
            {
                serviceAvailable = true;
                sessionDeadline = Environment.TickCount64 + 15000;
                heartbeatAt = Environment.TickCount64 + (token is null ? 30000 : 0);
                if (token is null) connectionRetry.Connected();
                connectionStatus = token is null ? "Connected." : "Loading your profile...";
            });
        }, "Connecting");
    }

    private bool ConnectionReady => serviceAvailable && (device is null || (client?.Connected == true && sessionReady));

    private void NotifyStatus(string message, bool error = false)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        toasts.Show("status", error ? "Unable to complete action" : "", message, TimeSpan.FromSeconds(error ? 10 : 6));
    }

    private bool Send(ClientCommand command)
    {
        bool sent = client?.Send(command) == true;
        if (!sent)
        {
            sessionReady = serviceAvailable = false;
            connectionStatus = "Connection interrupted. Reconnecting automatically...";
        }
        return sent;
    }

    private void Work(Func<RemoteClient, Action<Action>, Task> operation, string label)
    {
        if (busy || !IsOpen || client is null) return;
        var owner = client;
        void Complete(Action apply) => actions.Enqueue((owner, apply));
        busy = true;
        if (label != "Connecting") NotifyStatus(label + "...");
        _ = Task.Run(async () =>
        {
            try { await operation(owner, Complete).ConfigureAwait(false); }
            catch (Exception ex)
            {
                if (!ReferenceEquals(owner, client)) return;
                log.Write("client.operation_failed category=" + label, ex);
                Complete(() =>
                {
                    if (label != "Connecting") NotifyStatus(ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Conflict }
                        ? "That display name is unavailable. Please choose another."
                        : $"{label} failed. Please try again. Details were saved to the diagnostic log.", error: true);
                    if (label == "Connecting" || ex is HttpRequestException { StatusCode: null } or OperationCanceledException)
                    {
                        sessionReady = serviceAvailable = false;
                        connectionStatus = ex is InvalidDataException ? "The server requires a different plugin version. Please check for an update." :
                            "The game service is unavailable. We will keep trying automatically.";
                        connectionRetry.Failed(Environment.TickCount64);
                        if (label == "Connecting" && ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized })
                        {
                            // Keep the encrypted file for recovery; never silently reuse another server's credentials.
                            device = null; profile = null; room = null; chat.Clear(); screen = "welcome";
                            returningArtist = true;
                            NotifyStatus("Your saved sign-in is no longer valid. Recover your profile using your private code.", error: true);
                        }
                    }
                });
            }
            finally { Complete(() => busy = false); }
        });
    }

    public void Dispose()
    {
        CloseSession();
        disposed = true;
        updates.Dispose();
        audio.Dispose();
        painting.Dispose(); avatarEditor.Dispose(); avatars.Dispose(); art.Dispose();
        newRecovery = ""; recoveryInput = "";
    }
}
