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

public sealed class Peer(Guid id, WebSocket socket) : IGamePeer
{
    private readonly Channel<byte[]> queue = Channel.CreateBounded<byte[]>(128);
    private DateTimeOffset window = DateTimeOffset.UtcNow;
    private int commands, drawing, fills;
    private int queuedBytes;
    private DateTimeOffset profileAt;
    public Guid Id { get; } = id;
    public string? RoomCode { get; set; }
    public WebSocket Socket { get; } = socket;

    public void Send(ServerEvent message)
    {
        var bytes = Protocol.Encode(message);
        if (Interlocked.Add(ref queuedBytes, bytes.Length) > 1024 * 1024 || !queue.Writer.TryWrite(bytes))
        { Interlocked.Add(ref queuedBytes, -bytes.Length); Socket.Abort(); }
    }

    public bool Allow(string type)
    {
        if (type == "profile")
        {
            if ((DateTimeOffset.UtcNow - profileAt).TotalSeconds < 5) return false;
            profileAt = DateTimeOffset.UtcNow;
        }
        if ((DateTimeOffset.UtcNow - window).TotalSeconds >= 1)
        { window = DateTimeOffset.UtcNow; commands = drawing = fills = 0; }
        if (type == "draw") return ++drawing <= 90;
        if (type == "fill") return ++fills <= 4;
        return ++commands <= 8;
    }

    public async Task PumpAsync(CancellationToken cancel)
    {
        await foreach (var bytes in queue.Reader.ReadAllAsync(cancel))
        {
            Interlocked.Add(ref queuedBytes, -bytes.Length);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await Socket.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token);
        }
    }

    public void Complete() => queue.Writer.TryComplete();
    public void Close() => Socket.Abort();
}
