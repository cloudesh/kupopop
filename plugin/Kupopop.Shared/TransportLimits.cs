namespace Kupopop.Shared;

/// <summary>Allocation and liveness bounds shared by the two transport implementations.</summary>
public static class TransportLimits
{
    public const int ClientMessageBytes = 12_000;
    public const int ServerMessageBytes = 1024 * 1024;
    public const int QueueMessages = 128;
    // Startup sends a canvas, 16 portraits, 100 relationships, 30 recents and
    // 20 invitations before the send pump begins. Allow that whole bounded batch.
    public const int ServerQueueBytes = 2 * 1024 * 1024;
    public const int ClientQueueBytes = 4 * 1024 * 1024;
    public static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan ReconnectGrace = TimeSpan.FromSeconds(30);
}
