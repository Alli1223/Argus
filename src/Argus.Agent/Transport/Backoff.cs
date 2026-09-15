namespace Argus.Agent.Transport;

/// <summary>
/// Exponential backoff with jitter, so a fleet of agents does not reconnect in lockstep after an
/// outage: initial × 2^failures, capped at <paramref name="maximum"/>, ±20 % random.
/// </summary>
internal sealed class Backoff(TimeSpan initial, TimeSpan maximum)
{
    public int Failures { get; private set; }

    public TimeSpan NextDelay()
    {
        var exponential = initial.TotalSeconds * Math.Pow(2, Math.Min(Failures, 20));
        Failures++;
        var capped = Math.Min(exponential, maximum.TotalSeconds);
        var jittered = capped * (0.8 + Random.Shared.NextDouble() * 0.4);
        return TimeSpan.FromSeconds(Math.Min(jittered, maximum.TotalSeconds));
    }

    public void Reset() => Failures = 0;
}
