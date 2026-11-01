using System.Net.WebSockets;
using System.Threading.Channels;
using Kupopop.Shared;

namespace Kupopop.Server;

public interface IGamePeer
{
    Guid Id { get; }
    string? RoomCode { get; set; }
    void Send(ServerEvent message);
    bool Allow(string type);
    void Close();
}

public sealed class Peer(Guid id, WebSocket socket, InputRateLimiter? limiter = null) : IGamePeer
{
    private readonly Channel<byte[]> queue = Channel.CreateBounded<byte[]>(128);
    private readonly InputRateLimiter limits = limiter ?? new();
    private int queuedBytes;
    public Guid Id { get; } = id;
    public string? RoomCode { get; set; }
    public WebSocket Socket { get; } = socket;

    public void Send(ServerEvent message)
    {
        var bytes = Protocol.Encode(message);
        if (Interlocked.Add(ref queuedBytes, bytes.Length) > 1024 * 1024 || !queue.Writer.TryWrite(bytes))
        { Interlocked.Add(ref queuedBytes, -bytes.Length); Socket.Abort(); }
    }

    public bool Allow(string type) => limits.Allow(type);

    public async Task<byte[]?> ReceiveAsync(CancellationToken cancel, TimeSpan? timeout = null)
    {
        // Normal clients heartbeat every 15 seconds. A finite whole-message deadline
        // also prevents a sender holding a slot open by trickling fragments.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(45));
        var bytes = await Protocol.ReceiveAsync(Socket, 12000, deadline.Token);
        if (bytes is not null && !Allow("transport"))
            throw new InvalidDataException("Connection traffic budget exceeded.");
        return bytes;
    }

    public async Task PumpAsync(CancellationToken cancel)
    {
        try
        {
            await foreach (var bytes in queue.Reader.ReadAllAsync(cancel))
            {
                Interlocked.Add(ref queuedBytes, -bytes.Length);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await Socket.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token);
            }
        }
        finally { Socket.Abort(); }
    }

    public void Complete() => queue.Writer.TryComplete();
    public void Close() => Socket.Abort();
}
