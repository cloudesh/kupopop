namespace Kupopop.Shared;

/// <summary>Retries indefinitely, with a bounded delay so an outage does not flood the service.</summary>
public sealed class ConnectionRetry
{
    public int Attempts { get; private set; }
    public long NextAttempt { get; private set; }
    public bool Due(long now) => now >= NextAttempt;

    public void Failed(long now)
    {
        Attempts = Math.Min(Attempts + 1, 30);
        NextAttempt = now + Math.Min(30000L, 1000L << Math.Min(Attempts, 5));
    }

    public void Connected() { Attempts = 0; NextAttempt = 0; }
}
