using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;

namespace Kupopop.Shared;

public sealed class RemoteClient : IDisposable
{
    private readonly Uri endpoint;
    private readonly HttpClient http;
    private readonly Action<string, Exception?>? diagnostic;
    private readonly ConcurrentQueue<(ServerEvent Event, int Bytes)> events = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly object connectionGate = new();
    private readonly SemaphoreSlim sendGate = new(1, 1);
    private readonly SemaphoreSlim disconnectGate = new(1, 1);
    private int closing;
    private int queuedEventBytes;
    private int disposed;
    private ClientWebSocket? socket;
    private CancellationTokenSource? stop;
    private Channel<byte[]>? outgoing;
    private Task? connection;
    public bool Connected => Volatile.Read(ref closing) == 0 && socket?.State == WebSocketState.Open;
    public bool TryRead(out ServerEvent? message)
    {
        message = null;
        if (!events.TryDequeue(out var entry)) return false;
        Interlocked.Add(ref queuedEventBytes, -entry.Bytes);
        message = entry.Event;
        return true;
    }

    public RemoteClient(Uri endpoint, Action<string, Exception?>? diagnostic = null, bool allowTestHttp = false)
    {
        if (!endpoint.IsAbsoluteUri || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 ||
            endpoint.Fragment.Length != 0 || endpoint.AbsolutePath != "/" ||
            endpoint.HostNameType != UriHostNameType.Dns ||
            (endpoint.Scheme != "https" && !(allowTestHttp && endpoint.Scheme == "http" && endpoint.IsLoopback)))
            throw new ArgumentException("Use an HTTPS server hostname, without a path, credentials, or query string.");
        this.endpoint = endpoint;
        this.diagnostic = diagnostic;
        http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(15) };
    }

    public Task<AccountCredentials> CreateAsync(string name, bool shareProfile = false) => PostAsync("api/accounts", new AccountRequest(name, shareProfile));

    public async Task<InvitationInbox> CheckInvitationsAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/invitations");
        request.Headers.Authorization = new("Bearer", token);
        var inbox = await RequestAsync<InvitationInbox>(request, 32768).ConfigureAwait(false);
        if (inbox.Invitations is not { Length: <= 20 } || inbox.Invitations.Any(i =>
            i is null || !Protocol.ValidEvent(new("invitation", Invitation: i))))
            throw new InvalidDataException("Invalid invitation inbox.");
        return inbox;
    }
    public Task<AccountCredentials> RecoverAsync(string code) => PostAsync("api/recover", new RecoveryRequest(code));

    public async Task CheckHealthAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "health");
        var health = await RequestAsync<ServiceHealth>(request, 4096).ConfigureAwait(false);
        if (health?.Protocol != Protocol.Version)
            throw new InvalidDataException("The server protocol is incompatible. Update the plugin.");
    }

    private sealed record ServiceHealth(int Protocol);

    public async Task<AccountCredentials> RotateRecoveryAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/recovery-code");
        request.Headers.Authorization = new("Bearer", token);
        return ValidateCredentials(await RequestAsync<AccountCredentials>(request).ConfigureAwait(false));
    }

    private async Task<AccountCredentials> PostAsync<T>(string path, T body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, options: Protocol.Json) };
        return ValidateCredentials(await RequestAsync<AccountCredentials>(request).ConfigureAwait(false));
    }

    private async Task<T> RequestAsync<T>(HttpRequestMessage request, int maximumBytes = 65536)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        byte[] body = await BoundedHttp.ReadAsync(response, maximumBytes, timeout.Token).ConfigureAwait(false);
        try { return JsonSerializer.Deserialize<T>(body, Protocol.Json) ?? throw new InvalidDataException("The server returned an empty response."); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(body); }
    }

    private static AccountCredentials ValidateCredentials(AccountCredentials account)
    {
        static bool Secret(string? value) => value is { Length: 43 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
        if (account.AccountId == Guid.Empty || account.Profile is not { } profile || profile.Id != account.AccountId ||
            !Secret(account.AccessToken) || !Secret(account.RecoveryCode) || !Protocol.ValidName(profile.DisplayName) ||
            !Protocol.ValidAvatar(profile.AvatarPixels) || (profile.AvatarPreset is not null && AvatarPresets.Find(profile.AvatarPreset) is null))
            throw new InvalidDataException("The server returned an invalid account.");
        return account;
    }

    public async Task ConnectAsync(string accessToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref closing) != 0 || disposed != 0, this);
        await DisconnectAsync().ConfigureAwait(false);
        while (TryRead(out _)) { }
        ClientWebSocket ws;
        CancellationToken cancel;
        lock (connectionGate)
        {
            ObjectDisposedException.ThrowIf(closing != 0 || disposed != 0, this);
            stop = new CancellationTokenSource();
            cancel = stop.Token;
            socket = ws = new ClientWebSocket();
        }
        ws.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);
        ws.Options.SetRequestHeader("X-Kupopop-Protocol", Protocol.Version.ToString());
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        ws.Options.CollectHttpResponseDetails = true;
        var uri = new UriBuilder(endpoint) { Scheme = endpoint.Scheme == "https" ? "wss" : "ws", Path = "/ws" }.Uri;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel, lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try { await ws.ConnectAsync(uri, timeout.Token).ConfigureAwait(false); }
        catch (Exception ex)
        {
            diagnostic?.Invoke("websocket.connect_failed http_status=" + (int)ws.HttpStatusCode, ex);
            if (ws.HttpStatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.UpgradeRequired)
                throw new HttpRequestException("Sign-in rejected.", ex, ws.HttpStatusCode);
            throw;
        }
        lock (connectionGate)
        {
            ObjectDisposedException.ThrowIf(closing != 0 || disposed != 0, this);
            outgoing = Channel.CreateBounded<byte[]>(128);
            diagnostic?.Invoke("websocket.connected protocol=" + Protocol.Version, null);
            connection = RunAsync(ws, outgoing, cancel);
        }
    }

    public bool Send(ClientCommand command)
    {
        if (!Connected || outgoing is null) return false;
        if (outgoing.Writer.TryWrite(Protocol.Encode(command))) return true;
        diagnostic?.Invoke("websocket.send_queue_full", null);
        socket?.Abort();
        return false;
    }

    private async Task RunAsync(ClientWebSocket ws, Channel<byte[]> sends, CancellationToken cancel)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        var sending = Task.Run(async () =>
        {
            try
            {
                await foreach (var bytes in sends.Reader.ReadAllAsync(linked.Token))
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    await sendGate.WaitAsync(timeout.Token).ConfigureAwait(false);
                    try { await ws.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token).ConfigureAwait(false); }
                    finally { sendGate.Release(); }
                }
            }
            finally { linked.Cancel(); ws.Abort(); }
        }, CancellationToken.None);
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var bytes = await Protocol.ReceiveAsync(ws, 1024 * 1024, linked.Token).ConfigureAwait(false);
                if (bytes is null) break;
                var message = JsonSerializer.Deserialize<ServerEvent>(bytes, Protocol.Json);
                if (message is null || !Protocol.ValidEvent(message) || events.Count >= 512) throw new InvalidDataException("Inbound queue limit or invalid event.");
                if (Interlocked.Add(ref queuedEventBytes, bytes.Length) > 4 * 1024 * 1024)
                { Interlocked.Add(ref queuedEventBytes, -bytes.Length); throw new InvalidDataException("Inbound byte budget exceeded."); }
                events.Enqueue((message, bytes.Length));
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or InvalidDataException or JsonException)
        {
            if (!cancel.IsCancellationRequested)
            {
                diagnostic?.Invoke("websocket.receive_failed", ex);
            }
        }
        finally
        {
            linked.Cancel();
            sends.Writer.TryComplete();
            ws.Abort();
            try { await sending.ConfigureAwait(false); } catch (Exception ex) when (ex is WebSocketException or OperationCanceledException) { }
            if (!cancel.IsCancellationRequested) events.Enqueue((new("disconnected"), 0));
            diagnostic?.Invoke("websocket.disconnected", null);
        }
    }

    public async Task DisconnectAsync()
    {
        await disconnectGate.WaitAsync().ConfigureAwait(false);
        try
        {
            stop?.Cancel();
            socket?.Abort();
            if (connection is not null)
                try { await connection.ConfigureAwait(false); } catch (OperationCanceledException) { }
            connection = null;
            outgoing = null;
            socket?.Dispose(); socket = null;
            stop?.Dispose(); stop = null;
        }
        finally { disconnectGate.Release(); }
    }

    public async Task CloseAsync()
    {
        ClientWebSocket? ws;
        lock (connectionGate)
        {
            if (closing != 0 || disposed != 0) return;
            closing = 1;
            lifetime.Cancel();
            ws = socket;
        }
        try
        {
            if (ws?.State == WebSocketState.Open)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await sendGate.WaitAsync(timeout.Token).ConfigureAwait(false);
                try { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Window closed", timeout.Token).ConfigureAwait(false); }
                finally { sendGate.Release(); }
                if (connection is { } pending) await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException or InvalidOperationException)
        { diagnostic?.Invoke("websocket.close_interrupted", null); }
        finally { await DisconnectAsync().ConfigureAwait(false); Dispose(); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Interlocked.Exchange(ref closing, 1);
        lifetime.Cancel();
        stop?.Cancel();
        socket?.Abort();
        http.Dispose();
        // The receive task owns its active socket until cancellation completes.
        var pending = connection;
        var ws = socket;
        var cts = stop;
        _ = Task.Run(async () =>
        {
            if (pending is not null) try { await pending.ConfigureAwait(false); } catch (OperationCanceledException) { }
            ws?.Dispose(); cts?.Dispose(); lifetime.Dispose();
        });
    }
}
