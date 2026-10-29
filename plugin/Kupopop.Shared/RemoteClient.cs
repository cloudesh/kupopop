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
    private readonly ConcurrentQueue<ServerEvent> events = new();
    private ClientWebSocket? socket;
    private CancellationTokenSource? stop;
    private Channel<byte[]>? outgoing;
    private Task? connection;
    public bool Connected => socket?.State == WebSocketState.Open;
    public bool TryRead(out ServerEvent? message) => events.TryDequeue(out message);

    public RemoteClient(Uri endpoint, Action<string, Exception?>? diagnostic = null, bool allowTestHttp = false)
    {
        if (!endpoint.IsAbsoluteUri || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 ||
            endpoint.Fragment.Length != 0 || endpoint.AbsolutePath != "/" ||
            endpoint.HostNameType != UriHostNameType.Dns ||
            (endpoint.Scheme != "https" && !(allowTestHttp && endpoint.Scheme == "http" && endpoint.IsLoopback)))
            throw new ArgumentException("Use an HTTPS server hostname, without a path, credentials, or query string.");
        this.endpoint = endpoint;
        this.diagnostic = diagnostic;
        http = new HttpClient { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(15) };
    }

    public Task<AccountCredentials> CreateAsync(string name) => PostAsync("api/accounts", new AccountRequest(name));
    public Task<AccountCredentials> RecoverAsync(string code) => PostAsync("api/recover", new RecoveryRequest(code));

    public async Task<AccountCredentials> RotateRecoveryAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/recovery-code");
        request.Headers.Authorization = new("Bearer", token);
        using var response = await http.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AccountCredentials>(Protocol.Json).ConfigureAwait(false)
            ?? throw new InvalidDataException("Server returned no account.");
    }

    private async Task<AccountCredentials> PostAsync<T>(string path, T body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Protocol.Json).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Account request failed (HTTP {(int)response.StatusCode}).", null, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<AccountCredentials>(Protocol.Json).ConfigureAwait(false)
            ?? throw new InvalidDataException("Server returned no account.");
    }

    public async Task ConnectAsync(string accessToken)
    {
        await DisconnectAsync().ConfigureAwait(false);
        while (events.TryDequeue(out _)) { }
        stop = new CancellationTokenSource();
        socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);
        socket.Options.SetRequestHeader("X-Kupopop-Protocol", Protocol.Version.ToString());
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        socket.Options.CollectHttpResponseDetails = true;
        var uri = new UriBuilder(endpoint) { Scheme = endpoint.Scheme == "https" ? "wss" : "ws", Path = "/ws" }.Uri;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try { await socket.ConnectAsync(uri, timeout.Token).ConfigureAwait(false); }
        catch (Exception ex)
        {
            diagnostic?.Invoke("websocket.connect_failed http_status=" + (int)socket.HttpStatusCode, ex);
            throw;
        }
        outgoing = Channel.CreateBounded<byte[]>(128);
        diagnostic?.Invoke("websocket.connected protocol=1", null);
        connection = RunAsync(socket, outgoing, stop.Token);
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
                    await ws.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token).ConfigureAwait(false);
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
                if (message is null || events.Count >= 512) throw new InvalidDataException("Inbound queue limit or invalid event.");
                events.Enqueue(message);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or InvalidDataException or JsonException)
        {
            if (!cancel.IsCancellationRequested)
            {
                diagnostic?.Invoke("websocket.receive_failed", ex);
                events.Enqueue(new("disconnected", Error: $"Connection interrupted ({ex.GetType().Name}). Reconnect to restore your room within 30 seconds."));
            }
        }
        finally
        {
            linked.Cancel();
            sends.Writer.TryComplete();
            ws.Abort();
            try { await sending.ConfigureAwait(false); } catch (Exception ex) when (ex is WebSocketException or OperationCanceledException) { }
            if (!cancel.IsCancellationRequested) events.Enqueue(new("disconnected"));
            diagnostic?.Invoke("websocket.disconnected", null);
        }
    }

    public async Task DisconnectAsync()
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

    public void Dispose()
    {
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
            ws?.Dispose(); cts?.Dispose();
        });
    }
}
