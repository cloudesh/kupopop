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
    private readonly ConcurrentQueue<Action> actions = new();
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
    private bool updateCheckPending;
    private long nextUpdateCheck, remoteCursorAt;
    private readonly AvatarTextures avatars;
    private readonly UiAssets art;
    private readonly List<ChatLine> chat = [];
    private RemoteClient? client;
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
    private int wordChoiceCount = 4;
    private int roomSetupPage;
    private bool voteKickEnabled = true, voteSkipEnabled = true;
    private long heartbeatAt, statsAt, lastSync;
    private int received, draws;
    private TimeSpan serverOffset;
    public bool ShowDebug;

    public MainWindow(Plugin plugin, string directory, DiagnosticLog log)
        : base("Kupo Skribbl###KupopopPaper", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;
        this.log = log;
        avatars = new(log);
        art = new(log);
        vault = new(directory);
        Size = UiWindowGeometry.DefaultSize;
        SizeCondition = ImGuiCond.FirstUseEver;
        ConfigureClient();
    }

    private void ConfigureClient()
    {
        client?.Dispose();
        client = null; device = null; profile = null; room = null;
        serviceAvailable = sessionReady = false;
        newRecovery = "";
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
                    if (profile is not null) { sessionReady = serviceAvailable = true; connectionRetry.Connected(); }
                    if (savingProfile)
                    { savingProfile = false; screen = room is null ? "menu" : "game"; NotifyStatus("Profile saved."); }
                    if (profile is not null && screen != "profile") displayName = profile.DisplayName;
                    break;
                case "state" when message.Room is { } state:
                    if (state.Players.Length > 16) throw new InvalidDataException("Server player limit exceeded.");
                    bool differentRoom = room?.Code != state.Code;
                    bool newTurn = room?.TurnId != state.TurnId;
                    if (differentRoom) chat.Clear();
                    if (state.ResetCanvas || newTurn) canvas.Clear();
                    if (state.Canvas is { } pixels) canvas.Load(pixels);
                    if (newTurn || state.Canvas is not null) { undoPending = false; canUndoDrawing = state.CanUndo; }
                    if (newTurn || state.Phase != "drawing") painting.RemoteCursor = null;
                    if (state.Cursor is { } brush && state.DrawerId != device?.AccountId)
                    { painting.RemoteCursor = brush; remoteCursorAt = Environment.TickCount64; }
                    room = state;
                    serverOffset = state.ServerTime - DateTimeOffset.UtcNow;
                    if (screen != "profile") screen = "game";
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
                case "chat" when message.Chat is { } line:
                    chat.Add(line); if (chat.Count > 200) chat.RemoveAt(0); scrollChat = true;
                    break;
                case "left": room = null; chat.Clear(); screen = "menu"; NotifyStatus(message.Error ?? "You left the room."); break;
                case "error":
                    NotifyStatus(message.Error ?? "Server rejected the action.", error: true);
                    if (message.RejectedCommand == "profile") savingProfile = false;
                    if (message.RejectedCommand == "undo") { undoPending = false; canUndoDrawing = false; Send(new("sync")); }
                    log.Write("server.action_rejected action=" + SafeCommand(message.RejectedCommand) + " reason=" + SafeReason(message.Error));
                    if (room is not null && message.RejectedCommand == "draw" && Environment.TickCount64 - lastSync > 1000)
                    { lastSync = Environment.TickCount64; Send(new("sync")); }
                    break;
                case "disconnected":
                    savingProfile = false;
                    undoPending = false;
                    painting.RemoteCursor = null;
                    sessionReady = serviceAvailable = false;
                    connectionStatus = "Connection interrupted. Rejoining your drawing room automatically...";
                    if (connectionRetry.Due(Environment.TickCount64)) connectionRetry.Failed(Environment.TickCount64);
                    break;
            }
        }
        long now = Environment.TickCount64;
        if (now - remoteCursorAt > 2000) painting.RemoteCursor = null;
        if (!updateCheckPending && now >= nextUpdateCheck)
        {
            updateCheckPending = true; nextUpdateCheck = now + 600000;
            _ = Task.Run(async () =>
            {
                try
                {
                    var newer = await updates.CheckAsync(typeof(Plugin).Assembly.GetName().Version ?? new Version(0, 0));
                    if (newer is not null) actions.Enqueue(() =>
                    {
                        if (plugin.Config.DismissedUpdate != newer.ToString())
                            toasts.Show("update:" + newer, "An update is available", $"Kupo Skribbl {(newer.Build >= 0 ? newer.ToString(3) : newer.ToString())} is ready. Update through the Dalamud plugin installer.");
                    });
                }
                catch (Exception ex) { if (!disposed) log.Write("update.check_failed", ex); }
                finally { actions.Enqueue(() => updateCheckPending = false); }
            });
        }
        if (client?.Connected == true)
        {
            if (!sessionReady && !busy && now >= sessionDeadline)
            {
                connectionStatus = "The server took too long to load your profile. Trying again...";
                Work(async () => await client.DisconnectAsync(), "Reconnecting");
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
            sessionReady = false;
            sessionDeadline = Environment.TickCount64 + 15000;
            connectionRetry.Connected();
        });
        await client!.ConnectAsync(result.AccessToken);
        actions.Enqueue(() => NotifyStatus("Account ready. Save the recovery code below."));
    }

    private void Connect()
    {
        if (client is null || busy) return;
        // A fresh authenticated snapshot replaces potentially expired room state.
        // Room membership is restored by the server when its reconnect grace applies.
        if (device is not null) { room = null; chat.Clear(); if (screen == "game") screen = "menu"; }
        sessionReady = false;
        connectionStatus = device is null ? "Checking the game service..." : "Signing in to your Kupopop profile...";
        var activeClient = client;
        string? token = device?.AccessToken;
        Work(async () =>
        {
            await activeClient.CheckHealthAsync();
            if (token is not null) await activeClient.ConnectAsync(token);
            actions.Enqueue(() =>
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
        toasts.Show("status", error ? "Unable to complete action" : "Kupo Skribbl", message, TimeSpan.FromSeconds(error ? 10 : 6));
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

    private void Work(Func<Task> operation, string label)
    {
        if (busy) return;
        busy = true;
        if (label != "Connecting") NotifyStatus(label + "...");
        _ = Task.Run(async () =>
        {
            try { await operation().ConfigureAwait(false); }
            catch (Exception ex)
            {
                log.Write("client.operation_failed category=" + label, ex);
                actions.Enqueue(() =>
                {
                    if (label != "Connecting") NotifyStatus($"{label} failed. Please try again. Details were saved to the diagnostic log.", error: true);
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
            finally { actions.Enqueue(() => busy = false); }
        });
    }

    public void Dispose()
    {
        disposed = true;
        client?.Dispose();
        updates.Dispose();
        painting.Dispose(); avatarEditor.Dispose(); avatars.Dispose(); art.Dispose();
        newRecovery = ""; recoveryInput = "";
    }
}
