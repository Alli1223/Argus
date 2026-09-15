namespace Argus.Agent.Collection;

/// <summary>Helpers for turning ever-increasing OS counters into per-interval values.</summary>
internal static class Counters
{
    /// <summary>
    /// Difference between two readings of a monotonic counter. A smaller current value means the
    /// counter was reset (reboot, driver reload, wrap-around), which counts as no change.
    /// </summary>
    public static ulong Delta(ulong previous, ulong current) => current >= previous ? current - previous : 0;

    /// <summary>Per-second rate of a counter over <paramref name="seconds"/>.</summary>
    public static double Rate(ulong previous, ulong current, double seconds) =>
        seconds > 0 ? Delta(previous, current) / seconds : 0;
}
