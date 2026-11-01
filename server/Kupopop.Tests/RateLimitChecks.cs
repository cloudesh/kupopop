using Kupopop.Server;

static class RateLimitChecks
{
    public static void Run(Action<bool, string> check)
    {
        var clock = new Clock(); var limits = new InputRateLimiter(clock);
        check(Enumerable.Range(0, 120).All(_ => limits.Allow("draw")), "normal drawing delivery bursts above the old one-second count are accepted");
        check(Enumerable.Range(0, 60).All(_ => limits.Allow("draw")) && !limits.Allow("draw"), "drawing bursts remain bounded instead of disabling flood protection");
        clock.Advance(0.5);
        check(Enumerable.Range(0, 45).All(_ => limits.Allow("draw")) && !limits.Allow("draw"), "sustained drawing budget refills at the original 90 operations per second");
        foreach (string kind in new[] { "shape", "fill" })
        {
            check(Enumerable.Range(0, 16).All(_ => limits.Allow(kind)) && !limits.Allow(kind), "rapid short shape/fill gestures have a separate bounded burst budget");
            clock.Advance(1);
            check(Enumerable.Range(0, 4).All(_ => limits.Allow(kind)) && !limits.Allow(kind), "expensive shape/fill operations retain the original sustained rate limit");
        }
        check(Enumerable.Range(0, 8).All(_ => limits.Allow("chat")) && !limits.Allow("leave"), "drawing traffic never consumes the independent ordinary-command budget");
        clock.Advance(1);
        check(limits.Allow("profile") && !limits.Allow("profile"), "profile writes retain their five-second cooldown");
        clock.Advance(5);
        check(limits.Allow("profile"), "profile cooldown recovers after five seconds");
        check(Enumerable.Range(0, 50).All(_ => limits.Allow("cursor")) && !limits.Allow("cursor"), "cursor delivery bursts are bounded independently from drawing and chat");
    }

    private sealed class Clock : TimeProvider
    {
        private long tick;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => tick;
        public void Advance(double seconds) => tick += (long)(seconds * TimeSpan.TicksPerSecond);
    }
}
