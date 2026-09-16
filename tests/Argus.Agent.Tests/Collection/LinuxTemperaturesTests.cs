using Argus.Agent.Collection.Linux;
using Argus.Contracts.Agent;

namespace Argus.Agent.Tests.Collection;

/// <summary>Reads a copy of /sys laid out like a real laptop's, with a few broken sensors added.</summary>
public sealed class LinuxTemperaturesTests : IDisposable
{
    private readonly string _sys = Path.Combine(Path.GetTempPath(), "argus-agent-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_sys))
        {
            Directory.Delete(_sys, recursive: true);
        }
    }

    /// <summary>Adds /sys/class/hwmon/{entry} with its driver name, a device link and temperature files.</summary>
    private void Chip(string entry, string name, string? device, params (int Channel, string Input, string? Label)[] channels)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_sys, "class", "hwmon", entry)).FullName;
        File.WriteAllText(Path.Combine(directory, "name"), name + "\n");
        if (device is not null)
        {
            Directory.CreateSymbolicLink(Path.Combine(directory, "device"), device);
        }

        foreach (var (channel, input, label) in channels)
        {
            File.WriteAllText(Path.Combine(directory, $"temp{channel}_input"), input + "\n");
            if (label is not null)
            {
                File.WriteAllText(Path.Combine(directory, $"temp{channel}_label"), label + "\n");
            }
        }
    }

    private void Attribute(string entry, string file, string value) =>
        File.WriteAllText(Path.Combine(_sys, "class", "hwmon", entry, file), value + "\n");

    private static string[] Names(IEnumerable<TemperatureMetrics> readings) =>
        readings.Select(reading => $"{reading.Device}/{reading.Sensor}").ToArray();

    [Fact]
    public void Sensors_are_read_from_hwmon_chips()
    {
        Chip("hwmon1", "acpitz", "../../thermal_zone0", (1, "25000", null));
        Chip("hwmon2", "nvme", "../../nvme0", (1, "56850", "Composite"), (2, "56850", "Sensor 1"), (9, "60850", "Sensor 8"));
        Chip("hwmon5", "dell_smm", "../../../dell_smm_hwmon", (1, "76000", null), (2, "67000", null));
        Chip("hwmon6", "coretemp", "../../../coretemp.0", (1, "73000", "Package id 0"), (2, "76000", "Core 0"), (10, "79000", "Core 8"));
        Chip("hwmon7", "AC", "../../AC");

        var readings = new LinuxTemperatures(includeDrives: false, _sys).Collect();

        Assert.Equal(
            [
                "acpitz/temp1",
                "nvme0/Composite", "nvme0/Sensor 1", "nvme0/Sensor 8",
                "dell_smm/temp1", "dell_smm/temp2",
                "coretemp/Package id 0", "coretemp/Core 0", "coretemp/Core 8",
            ],
            Names(readings));
        Assert.Equal(56.85, readings[1].Celsius, precision: 6);
        Assert.Equal(79, readings[^1].Celsius, precision: 6);
    }

    [Fact]
    public void Unreadable_disconnected_faulty_and_disabled_sensors_are_skipped()
    {
        Chip("hwmon0", "nct6775", "../../../nct6775.656",
            (1, "38000", "SYSTIN"), (2, "", "CPUTIN"), (3, "-128000", "AUXTIN0"), (4, "127000", "AUXTIN1"),
            (5, "41000", "AUXTIN2"), (6, "42000", "AUXTIN3"));
        Attribute("hwmon0", "temp5_fault", "1");
        Attribute("hwmon0", "temp6_enable", "0");

        var readings = new LinuxTemperatures(includeDrives: false, _sys).Collect();

        Assert.Equal(["nct6775/SYSTIN"], Names(readings));
    }

    [Fact]
    public void Sata_drives_are_only_read_when_asked()
    {
        var disk = Directory.CreateDirectory(Path.Combine(_sys, "devices", "0:0:0:0", "block", "sda")).Parent!.Parent!.FullName;
        Chip("hwmon3", "drivetemp", disk, (1, "34000", null));
        Chip("hwmon4", "k10temp", "../../../0000:00:18.3", (1, "48000", "Tctl"));

        Assert.Equal(["k10temp/Tctl"], Names(new LinuxTemperatures(includeDrives: false, _sys).Collect()));
        Assert.Equal(["sda/temp1", "k10temp/Tctl"], Names(new LinuxTemperatures(includeDrives: true, _sys).Collect()));
    }

    [Fact]
    public void Chips_with_the_same_driver_are_told_apart_by_their_device()
    {
        Chip("hwmon1", "coretemp", "../../../coretemp.0", (1, "50000", "Package id 0"));
        Chip("hwmon2", "coretemp", "../../../coretemp.1", (1, "52000", "Package id 1"));
        Chip("hwmon3", "amdgpu", "../../../0000:03:00.0", (1, "45000", "edge"));
        Chip("hwmon4", "amdgpu", "../../../0000:04:00.0", (1, "47000", "edge"));
        Chip("hwmon5", "acpitz", null, (1, "30000", null));
        Chip("hwmon6", "acpitz", null, (1, "31000", null), (2, "32000", "temp1"));

        var readings = new LinuxTemperatures(includeDrives: false, _sys).Collect();

        Assert.Equal(
            [
                "coretemp.0/Package id 0", "coretemp.1/Package id 1",
                "amdgpu 0000:03:00.0/edge", "amdgpu 0000:04:00.0/edge",
                "acpitz hwmon5/temp1", "acpitz hwmon6/temp1", "acpitz hwmon6/temp1 (temp2)",
            ],
            Names(readings));
    }

    [Fact]
    public void Thermal_zones_stand_in_on_machines_without_hwmon_chips()
    {
        foreach (var (zone, type, temp) in new[] { ("thermal_zone0", "cpu-thermal", "48312"), ("thermal_zone1", "cpu-thermal", "47000"), ("thermal_zone2", "gpu-thermal", "") })
        {
            var directory = Directory.CreateDirectory(Path.Combine(_sys, "class", "thermal", zone)).FullName;
            File.WriteAllText(Path.Combine(directory, "type"), type + "\n");
            File.WriteAllText(Path.Combine(directory, "temp"), temp + "\n");
        }

        Directory.CreateDirectory(Path.Combine(_sys, "class", "hwmon"));

        var readings = new LinuxTemperatures(includeDrives: false, _sys).Collect();

        Assert.Equal(["thermal/cpu-thermal", "thermal/cpu-thermal (thermal_zone1)"], Names(readings));
        Assert.Equal(48.312, readings[0].Celsius, precision: 6);
    }

    [Fact]
    public void Machines_without_sensors_report_none()
    {
        Assert.Empty(new LinuxTemperatures(includeDrives: true, _sys).Collect());
    }

    [Theory]
    [InlineData("45500\n", 45.5)]
    [InlineData("-5000", -5.0)]
    [InlineData("0", 0.0)]
    [InlineData("-40000", null)]
    [InlineData("125000", null)]
    [InlineData("", null)]
    [InlineData("n/a", null)]
    public void Millidegrees_are_parsed_and_implausible_values_dropped(string text, double? celsius)
    {
        Assert.Equal(celsius, LinuxTemperatures.ParseMillidegrees(text));
    }
}
