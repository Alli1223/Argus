using Argus.Contracts.Agent;

namespace Argus.Agent.Collection;

/// <summary>Reads the machine's temperature sensors.</summary>
internal interface ITemperatureSource
{
    /// <summary>Every sensor that gave a believable reading; empty on machines without any the agent can read.</summary>
    IReadOnlyList<TemperatureMetrics> Collect();
}

/// <summary>For systems whose sensors the agent cannot read: reports none.</summary>
internal sealed class NoTemperatures : ITemperatureSource
{
    public IReadOnlyList<TemperatureMetrics> Collect() => [];
}

internal static class TemperatureReadings
{
    /// <summary>
    /// Sensors that are not connected, or not ready, report values such as -128, -40, 127 or 0 K. No
    /// computer runs outside this range, so readings beyond it are dropped rather than charted.
    /// </summary>
    public static bool IsPlausible(double celsius) => celsius is > -40.0 and < 125.0;
}
