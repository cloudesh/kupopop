namespace Kupopop.Server;

/// <summary>Reconnects retain their account's budgets and profile-write cooldown.</summary>
public sealed class AccountInputLimits
{
    // Only authenticated IDs enter this map; AccountStore caps their total at 10,000.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, InputRateLimiter> accounts = new();
    public InputRateLimiter For(Guid id) => accounts.GetOrAdd(id, _ => new InputRateLimiter());
}

/// <summary>Bounded bursts tolerate network delivery jitter without raising sustained traffic limits.</summary>
public sealed class InputRateLimiter(TimeProvider? provider = null)
{
    private readonly TimeProvider clock = provider ?? TimeProvider.System;
    private readonly Dictionary<string, Bucket> buckets = [];
    private long? profileAt;

    public bool Allow(string type)
    {
        long now = clock.GetTimestamp();
        if (type == "profile" && profileAt is { } previous && clock.GetElapsedTime(previous, now).TotalSeconds < 5) return false;
        var (key, capacity, refill) = type switch
        {
            "draw" => ("draw", 180d, 90d),
            "shape" => ("shape", 16d, 4d),
            "fill" => ("fill", 16d, 4d),
            "cursor" => ("cursor", 50d, 25d),
            _ => ("commands", 8d, 8d),
        };
        if (!buckets.TryGetValue(key, out var bucket)) buckets[key] = bucket = new(capacity, now);
        double elapsed = Math.Max(0, clock.GetElapsedTime(bucket.At, now).TotalSeconds);
        bucket.Tokens = Math.Min(capacity, bucket.Tokens + elapsed * refill); bucket.At = now;
        if (bucket.Tokens < 1) return false;
        bucket.Tokens--;
        if (type == "profile") profileAt = now;
        return true;
    }

    private sealed class Bucket(double tokens, long at)
    {
        public double Tokens = tokens;
        public long At = at;
    }
}
