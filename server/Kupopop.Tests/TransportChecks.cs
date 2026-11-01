using System.Net.WebSockets;
using Kupopop.Server;

internal static class TransportChecks
{
    public static void Run(Action<bool, string> check)
    {
        foreach (bool fragmented in new[] { false, true })
        {
            using var socket = new StalledSocket(fragmented);
            var peer = new Peer(Guid.NewGuid(), socket);
            bool expired = false;
            try { peer.ReceiveAsync(CancellationToken.None, TimeSpan.FromMilliseconds(30)).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { expired = true; }
            check(expired, fragmented ? "partial messages cannot retain a connection beyond the whole-message deadline"
                : "idle sockets have a finite receive deadline while normal heartbeats keep sessions alive");
        }
        using var immediate = new StalledSocket(false, immediate: true);
        var limited = new Peer(Guid.NewGuid(), immediate);
        for (int i = 0; i < 384; i++) limited.ReceiveAsync(CancellationToken.None).GetAwaiter().GetResult();
        bool rejected = false;
        try { limited.ReceiveAsync(CancellationToken.None).GetAwaiter().GetResult(); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "receive path closes excessive complete-message traffic before JSON dispatch");
    }

    private sealed class StalledSocket(bool fragmented, bool immediate = false) : WebSocket
    {
        private bool sent;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override void Dispose() { }
        public override Task CloseAsync(WebSocketCloseStatus status, string? text, CancellationToken cancel) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? text, CancellationToken cancel) => Task.CompletedTask;
        public override Task SendAsync(ArraySegment<byte> data, WebSocketMessageType type, bool end, CancellationToken cancel) => Task.CompletedTask;
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancel)
        {
            if (immediate) return new(0, WebSocketMessageType.Text, true);
            if (fragmented && !sent) { sent = true; buffer.Array![buffer.Offset] = (byte)'{'; return new(1, WebSocketMessageType.Text, false); }
            await Task.Delay(Timeout.Infinite, cancel);
            throw new InvalidOperationException("Unreachable.");
        }
    }
}
