using System.Globalization;
using Argus.Contracts.Agent;

namespace Argus.Agent.Collection.Linux;

/// <summary>
/// Temperatures from the kernel's hardware monitoring drivers (/sys/class/hwmon), or from thermal zones
/// on machines without any. SATA drives (the drivetemp driver) are left out unless
/// <paramref name="includeDrives"/> is set: on some drives, reading the temperature resets the spin-down
/// timer, so drives meant to sleep would never do so.
/// </summary>
internal sealed class LinuxTemperatures(bool includeDrives, string sysRoot = "/sys") : ITemperatureSource
{
    private const string DriveTemperatureDriver = "drivetemp";

    private sealed record Chip(string Path, string Name, List<int> Channels);

    public IReadOnlyList<TemperatureMetrics> Collect()
    {
        try
        {
            var chips = FindChips();
            return chips.Count > 0 ? ReadChips(chips) : ReadThermalZones();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A device disappearing mid-read is not worth losing the rest of the sample over.
            return [];
        }
    }

    /// <summary>Hardware monitoring chips with at least one temperature input.</summary>
    private List<Chip> FindChips()
    {
        var directory = Path.Combine(sysRoot, "class", "hwmon");
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var chips = new List<Chip>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
        {
            var name = LinuxFiles.ReadOrEmpty(Path.Combine(entry, "name")).Trim();
            if (name == DriveTemperatureDriver && !includeDrives)
            {
                continue;
            }

            var channels = Directory.EnumerateFiles(entry, "temp*_input")
                .Select(file => Path.GetFileName(file)["temp".Length..^"_input".Length])
                .Select(number => int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var channel) ? channel : -1)
                .Where(channel => channel >= 0)
                .Order()
                .ToList();

            if (channels.Count > 0)
            {
                chips.Add(new Chip(entry, name.Length > 0 ? name : Path.GetFileName(entry), channels));
            }
        }

        return chips;
    }

    private static List<TemperatureMetrics> ReadChips(List<Chip> chips)
    {
        var shared = chips.GroupBy(chip => chip.Name).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
        var readings = new List<TemperatureMetrics>();

        foreach (var chip in chips)
        {
            var device = DeviceName(chip, shared.Contains(chip.Name));
            var labels = new HashSet<string>(StringComparer.Ordinal);

            foreach (var channel in chip.Channels)
            {
                var prefix = Path.Combine(chip.Path, $"temp{channel}");
                if (ReadIfPresent(prefix + "_enable") == "0" || ReadIfPresent(prefix + "_fault") == "1"
                    || ParseMillidegrees(LinuxFiles.ReadOrEmpty(prefix + "_input")) is not { } celsius)
                {
                    continue;
                }

                var label = ReadIfPresent(prefix + "_label") is { Length: > 0 } text ? text : $"temp{channel}";
                readings.Add(new TemperatureMetrics
                {
                    Device = device,
                    Sensor = labels.Add(label) ? label : $"{label} (temp{channel})",
                    Celsius = celsius,
                });
            }
        }

        return readings;
    }

    /// <summary>
    /// A name for a chip that stays the same from one reading to the next. Drives are named after their
    /// device ("nvme0", "sda"), so adding a drive does not rename the others. Other chips go by their
    /// driver's name, told apart by their device when a machine has several ("coretemp.0", "coretemp.1").
    /// </summary>
    private static string DeviceName(Chip chip, bool shared)
    {
        var device = Path.Combine(chip.Path, "device");
        var block = Path.Combine(device, "block");
        if (Directory.Exists(block)
            && Directory.EnumerateFileSystemEntries(block).Select(entry => Path.GetFileName(entry)).Order(StringComparer.Ordinal).FirstOrDefault() is { } disk)
        {
            return disk;
        }

        var target = LinkName(device);
        if (chip.Name == "nvme" && target is not null && target.StartsWith("nvme", StringComparison.Ordinal))
        {
            return target;
        }

        if (!shared)
        {
            return chip.Name;
        }

        return target is null ? $"{chip.Name} {Path.GetFileName(chip.Path)}"
            : target.StartsWith(chip.Name, StringComparison.Ordinal) ? target
            : $"{chip.Name} {target}";
    }

    /// <summary>Thermal zones, for machines (often single-board computers and virtual machines) without hwmon chips.</summary>
    private List<TemperatureMetrics> ReadThermalZones()
    {
        var directory = Path.Combine(sysRoot, "class", "thermal");
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var readings = new List<TemperatureMetrics>();
        var types = new HashSet<string>(StringComparer.Ordinal);
        foreach (var zone in Directory.EnumerateFileSystemEntries(directory, "thermal_zone*").Order(StringComparer.Ordinal))
        {
            if (ParseMillidegrees(LinuxFiles.ReadOrEmpty(Path.Combine(zone, "temp"))) is not { } celsius)
            {
                continue;
            }

            var type = LinuxFiles.ReadOrEmpty(Path.Combine(zone, "type")).Trim() is { Length: > 0 } text ? text : Path.GetFileName(zone);
            readings.Add(new TemperatureMetrics
            {
                Device = "thermal",
                Sensor = types.Add(type) ? type : $"{type} ({Path.GetFileName(zone)})",
                Celsius = celsius,
            });
        }

        return readings;
    }

    /// <summary>A sensor file's value in degrees Celsius, or null when it holds no believable reading.</summary>
    internal static double? ParseMillidegrees(string text) =>
        long.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var millidegrees)
        && TemperatureReadings.IsPlausible(millidegrees / 1000.0)
            ? millidegrees / 1000.0
            : null;

    // Optional attributes are checked for first: most chips lack them, and a missing file would throw.
    private static string? ReadIfPresent(string path) => File.Exists(path) ? LinuxFiles.ReadOrEmpty(path).Trim() : null;

    private static string? LinkName(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget is { } target ? Path.GetFileName(target.TrimEnd('/')) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
