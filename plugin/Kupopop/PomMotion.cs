namespace Kupopop;

/// <summary>A bounded, damped spring driven only by movement of our own window.</summary>
public sealed class PomMotion
{
    public float Angle { get; private set; }
    private float velocity, previousX;
    private bool initialized;

    public void Nudge(float impulse = 2.8f)
    {
        if (float.IsFinite(impulse)) velocity = Math.Clamp(velocity + impulse, -5, 5);
    }

    public void Step(float windowX, float elapsed, bool enabled)
    {
        if (!initialized) { initialized = true; previousX = windowX; }
        float movement = Math.Clamp(windowX - previousX, -80, 80);
        previousX = windowX;
        if (!enabled || !float.IsFinite(elapsed) || elapsed <= 0 || elapsed > 0.25f)
        { Angle = velocity = 0; return; }
        velocity = Math.Clamp(velocity - movement * 0.018f, -5, 5);
        int steps = Math.Max(1, (int)MathF.Ceiling(elapsed * 120));
        float dt = elapsed / steps;
        for (int i = 0; i < steps; i++)
        {
            velocity += (-55 * Angle - 8 * velocity) * dt;
            Angle = Math.Clamp(Angle + velocity * dt, -0.62f, 0.62f);
        }
        if (MathF.Abs(Angle) + MathF.Abs(velocity) < 0.001f) Angle = velocity = 0;
    }
}
