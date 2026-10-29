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
    private readonly PixelCanvas avatar = new(64, 64);
    private readonly AvatarTextures avatars = new();
    private readonly UiAssets art;
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
    private bool busy, disposed, publicRoom, savedRecovery, revealRecovery, avatarReset, scrollChat, savingProfile;
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
        art = new(log);
        vault = new(directory);
        serverUrl = plugin.Config.ServerUrl;
        Size = new(1240, 840);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new() { MinimumSize = new(1024, 680), MaximumSize = new(float.MaxValue) };
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
                    if (savingProfile)
                    { savingProfile = false; screen = room is null ? "menu" : "game"; status = "Profile saved."; }
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
                    if (message.RejectedCommand == "profile") savingProfile = false;
                    log.Write("server.action_rejected action=" + SafeCommand(message.RejectedCommand) + " reason=" + SafeReason(message.Error));
                    if (room is not null && message.RejectedCommand == "draw" && Environment.TickCount64 - lastSync > 1000)
                    { lastSync = Environment.TickCount64; Send(new("sync")); }
                    break;
                case "disconnected":
                    savingProfile = false;
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
        if (!sent) status = "Not connected. Open Connection and reconnect.";
        return sent;
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
        painting.Dispose(); avatarEditor.Dispose(); avatars.Dispose(); art.Dispose();
        newRecovery = ""; recoveryInput = "";
    }
}
