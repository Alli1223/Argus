using System.Runtime.Versioning;
using Argus.Contracts.Agent;

namespace Argus.Agent.Collection.Windows;

/// <summary>
/// ACPI thermal zones, through the "Thermal Zone Information" performance counters, then graphics
/// adapters ("gpu0", …) and drives ("disk0", … by their PhysicalDrive number). Many machines have one
/// zone or none, and some report a value that never changes: Windows offers no way to read the
/// processor's own sensors without a kernel driver.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsTemperatures(bool includeDrives, ILogger<WindowsTemperatures> logger) : ITemperatureSource, IDisposable
{
    private const string ZoneTemperature = @"\Thermal Zone Information(*)\Temperature";
    private const double KelvinOffset = 273.15;

    private PdhQuery? _query;
    private bool _unavailable;
    private readonly HashSet<string> _failed = [];

    public IReadOnlyList<TemperatureMetrics> Collect() =>
        [.. ReadZones(), .. Read("Graphics adapter", ReadGpus), .. Read("Drive", ReadDrives)];

    private IEnumerable<TemperatureMetrics> ReadZones()
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
            logger.LogInformation("Thermal zone temperatures are unavailable: {Reason}", ex.Message);
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

    private static List<TemperatureMetrics> ReadGpus()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        return WindowsSensors.ReadGpus()
            .Select((gpu, i) => new TemperatureMetrics
            {
                Device = $"gpu{i}",
                Sensor = names.Add(gpu.Name) ? gpu.Name : $"{gpu.Name} ({i})",
                Celsius = Math.Round(gpu.Celsius, 2),
            })
            .ToList();
    }

    private List<TemperatureMetrics> ReadDrives() =>
        WindowsSensors.ReadDrives(includeDrives)
            .SelectMany(drive => drive.Celsius.Select((celsius, i) => new TemperatureMetrics
            {
                Device = $"disk{drive.Number}",
                Sensor = i == 0 ? drive.Model : $"{drive.Model} ({i + 1})",
                Celsius = celsius,
            }))
            .ToList();

    /// <summary>One kind of sensor; a failure leaves the others' readings, and is logged the first time only.</summary>
    private IEnumerable<TemperatureMetrics> Read(string kind, Func<List<TemperatureMetrics>> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or IOException or UnauthorizedAccessException)
        {
            if (_failed.Add(kind))
            {
                logger.LogInformation("{Kind} temperatures are unavailable: {Reason}", kind, ex.Message);
            }

            return [];
        }
    }

    public void Dispose() => _query?.Dispose();

    /// <summary>Zones are named by their ACPI path, such as <c>\_TZ.TZ00</c>; the last part is the name.</summary>
    private static string ZoneName(string instance) =>
        instance.StartsWith(@"\_TZ.", StringComparison.OrdinalIgnoreCase) && instance.Length > 5 ? instance[5..] : instance;
}
