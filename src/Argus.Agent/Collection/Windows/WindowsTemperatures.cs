using System.Runtime.Versioning;
using Argus.Contracts.Agent;

namespace Argus.Agent.Collection.Windows;

/// <summary>
/// ACPI thermal zones, through the "Thermal Zone Information" performance counters. Many machines have
/// one zone or none, and some report a value that never changes: Windows offers no general way to read
/// processor or drive sensors without a vendor's driver.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsTemperatures(ILogger<WindowsTemperatures> logger) : ITemperatureSource, IDisposable
{
    private const string ZoneTemperature = @"\Thermal Zone Information(*)\Temperature";
    private const double KelvinOffset = 273.15;

    private PdhQuery? _query;
    private bool _unavailable;

    public IReadOnlyList<TemperatureMetrics> Collect()
    {
        if (_unavailable)
        {
            return [];
        }

        try
        {
            _query ??= new PdhQuery([ZoneTemperature]);
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException)
        {
            _unavailable = true;
            logger.LogInformation("Temperatures are unavailable: {Reason}", ex.Message);
            return [];
        }

        _query.Collect();
        var readings = new List<TemperatureMetrics>();
        foreach (var (instance, kelvin) in _query.ReadInstances(ZoneTemperature))
        {
            var celsius = kelvin - KelvinOffset;
            if (TemperatureReadings.IsPlausible(celsius))
            {
                readings.Add(new TemperatureMetrics { Device = "acpitz", Sensor = ZoneName(instance), Celsius = Math.Round(celsius, 2) });
            }
        }

        return readings;
    }

    public void Dispose() => _query?.Dispose();

    /// <summary>Zones are named by their ACPI path, such as <c>\_TZ.TZ00</c>; the last part is the name.</summary>
    private static string ZoneName(string instance) =>
        instance.StartsWith(@"\_TZ.", StringComparison.OrdinalIgnoreCase) && instance.Length > 5 ? instance[5..] : instance;
}
