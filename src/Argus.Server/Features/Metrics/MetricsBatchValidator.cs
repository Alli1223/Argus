using Argus.Contracts.Agent;

namespace Argus.Server.Features.Metrics;

/// <summary>Checks and cleans agent batches before they reach the database.</summary>
public static class MetricsBatchValidator
{
    private const int NameLength = 256;
    private const int FsTypeLength = 64;
    private const int StateLength = 32;
    private const int ImageLength = 512;
    private const int PortLength = 64;
    private const int ProblemLength = 1000;
    private const double AbsoluteZero = -273.15;

    private static readonly HashSet<string> ContainerStates = ["created", "running", "paused", "restarting", "removing", "exited", "dead"];
    private static readonly HashSet<string> ContainerHealth = ["starting", "healthy", "unhealthy"];
    private static readonly HashSet<string> RestartPolicies = ["no", "always", "unless-stopped", "on-failure"];
    private static readonly HashSet<string> GuestTypes = ["qemu", "lxc"];
    private static readonly HashSet<string> GuestStatuses = ["running", "stopped", "paused"];
    private const double MaxCelsius = 1000;

    /// <summary>Problems that make the whole batch unacceptable; the agent should not resend it.</summary>
    public static string? ValidateShape(MetricsBatch batch) => batch.Samples.Count switch
    {
        0 => "The batch contains no samples.",
        > AgentLimits.MaxSamplesPerBatch => $"A batch may contain at most {AgentLimits.MaxSamplesPerBatch} samples.",
        _ => null,
    };

    /// <summary>
    /// Returns the samples worth storing. Samples outside the accepted time window are dropped rather
    /// than failing the batch, so one bad reading cannot make an agent resend the same batch forever.
    /// Out-of-range values are clamped, oversized lists truncated and timestamps converted to UTC.
    /// </summary>
    public static List<MetricSample> Sanitize(
        IEnumerable<MetricSample> samples, DateTimeOffset now, TimeSpan maxAge, TimeSpan maxClockSkew, out int dropped)
    {
        var accepted = new List<MetricSample>();
        dropped = 0;

        foreach (var sample in samples)
        {
            if (sample.Timestamp < now - maxAge || sample.Timestamp > now + maxClockSkew)
            {
                dropped++;
                continue;
            }

            accepted.Add(Clean(sample));
        }

        return accepted;
    }

    private static MetricSample Clean(MetricSample sample) => sample with
    {
        Timestamp = sample.Timestamp.ToUniversalTime(),
        Cpu = new CpuMetrics
        {
            UsagePercent = Percent(sample.Cpu.UsagePercent),
            UserPercent = Percent(sample.Cpu.UserPercent),
            SystemPercent = Percent(sample.Cpu.SystemPercent),
            IowaitPercent = Percent(sample.Cpu.IowaitPercent),
            StealPercent = Percent(sample.Cpu.StealPercent),
        },
        Memory = new MemoryMetrics
        {
            TotalBytes = Math.Max(0, sample.Memory.TotalBytes),
            UsedBytes = Math.Max(0, sample.Memory.UsedBytes),
            AvailableBytes = Math.Max(0, sample.Memory.AvailableBytes),
            CachedBytes = sample.Memory.CachedBytes is { } cached ? Math.Max(0, cached) : null,
            SwapTotalBytes = Math.Max(0, sample.Memory.SwapTotalBytes),
            SwapUsedBytes = Math.Max(0, sample.Memory.SwapUsedBytes),
        },
        Load = sample.Load is { } load
            ? new LoadMetrics { Load1 = Rate(load.Load1), Load5 = Rate(load.Load5), Load15 = Rate(load.Load15) }
            : null,
        DiskIo = sample.DiskIo is { } disk
            ? new DiskIoMetrics
            {
                ReadBytesPerSec = Rate(disk.ReadBytesPerSec),
                WriteBytesPerSec = Rate(disk.WriteBytesPerSec),
                ReadOpsPerSec = Rate(disk.ReadOpsPerSec),
                WriteOpsPerSec = Rate(disk.WriteOpsPerSec),
                UtilizationPercent = Percent(disk.UtilizationPercent),
            }
            : null,
        Network = sample.Network is { } network
            ? new NetworkMetrics { RxBytesPerSec = Rate(network.RxBytesPerSec), TxBytesPerSec = Rate(network.TxBytesPerSec) }
            : null,
        ProcessCount = Math.Max(0, sample.ProcessCount),
        UptimeSeconds = Math.Max(0, sample.UptimeSeconds),
        Filesystems = sample.Filesystems
            .Where(fs => !string.IsNullOrWhiteSpace(fs.MountPoint))
            .Take(AgentLimits.MaxFilesystemsPerSample)
            .Select(fs => new FilesystemMetrics
            {
                MountPoint = Clip(fs.MountPoint, NameLength),
                Device = ClipOptional(fs.Device, NameLength),
                FsType = ClipOptional(fs.FsType, FsTypeLength),
                TotalBytes = Math.Max(0, fs.TotalBytes),
                UsedBytes = Math.Max(0, fs.UsedBytes),
                AvailableBytes = Math.Max(0, fs.AvailableBytes),
                InodesTotal = fs.InodesTotal is { } total ? Math.Max(0, total) : null,
                InodesUsed = fs.InodesUsed is { } used ? Math.Max(0, used) : null,
            })
            .ToList(),
        Interfaces = sample.Interfaces
            .Where(nic => !string.IsNullOrWhiteSpace(nic.Name))
            .Take(AgentLimits.MaxInterfacesPerSample)
            .Select(nic => new NetworkInterfaceMetrics
            {
                Name = Clip(nic.Name, NameLength),
                RxBytesPerSec = Rate(nic.RxBytesPerSec),
                TxBytesPerSec = Rate(nic.TxBytesPerSec),
                RxPacketsPerSec = Rate(nic.RxPacketsPerSec),
                TxPacketsPerSec = Rate(nic.TxPacketsPerSec),
                RxErrorsPerSec = Rate(nic.RxErrorsPerSec),
                TxErrorsPerSec = Rate(nic.TxErrorsPerSec),
            })
            .ToList(),
        Temperatures = sample.Temperatures
            .Where(reading => !string.IsNullOrWhiteSpace(reading.Device) && !string.IsNullOrWhiteSpace(reading.Sensor)
                && reading.Celsius is >= AbsoluteZero and <= MaxCelsius)
            .Select(reading => new TemperatureMetrics
            {
                // Series are keyed "{device}/{sensor}", so a slash may only follow the device name.
                Device = Clip(reading.Device.Trim().Replace('/', '-'), NameLength),
                Sensor = Clip(reading.Sensor.Trim(), NameLength),
                Celsius = reading.Celsius,
            })
            // One reading per sensor: the database keys readings by host, device, sensor and time.
            .DistinctBy(reading => (reading.Device, reading.Sensor))
            .Take(AgentLimits.MaxTemperaturesPerSample)
            .ToList(),
        // One reading per UPS: the database keys readings by host, UPS and time.
        Ups = sample.Ups
            .Where(ups => !string.IsNullOrWhiteSpace(ups.Name))
            .DistinctBy(ups => ups.Name.Trim())
            .Take(AgentLimits.MaxUpsPerSample)
            .Select(ups => new UpsMetrics
            {
                Name = Clip(ups.Name.Trim(), NameLength),
                Model = ClipOptional(ups.Model, NameLength),
                Status = Clip((ups.Status ?? "").Trim().ToUpperInvariant(), StateLength * 2),
                BatteryChargePercent = Percent(Finite(ups.BatteryChargePercent)),
                BatteryRuntimeSeconds = Finite(ups.BatteryRuntimeSeconds) is { } runtime ? Math.Max(0, runtime) : null,
                LoadPercent = Finite(ups.LoadPercent) is { } load ? Math.Max(0, load) : null,
                InputVoltage = Finite(ups.InputVoltage) is { } input ? Math.Max(0, input) : null,
                OutputVoltage = Finite(ups.OutputVoltage) is { } output ? Math.Max(0, output) : null,
                BatteryVoltage = Finite(ups.BatteryVoltage) is { } battery ? Math.Max(0, battery) : null,
            })
            .ToList(),
        Containers = sample.Containers is { } report ? Clean(report) : null,
        Proxmox = sample.Proxmox is { } proxmox ? Clean(proxmox) : null,
        ContainerUsage = sample.ContainerUsage?
            .Where(usage => !string.IsNullOrWhiteSpace(usage.Name))
            .DistinctBy(usage => usage.Name)
            .Take(AgentLimits.MaxContainers)
            .Select(usage => new ContainerUsage
            {
                Name = Clip(usage.Name, NameLength),
                CpuPercent = Percent(usage.CpuPercent),
                MemoryBytes = Math.Max(0, usage.MemoryBytes),
                MemoryLimitBytes = usage.MemoryLimitBytes is > 0 and var limit ? limit : null,
                NetRxBytesPerSec = usage.NetRxBytesPerSec is { } rx ? Rate(rx) : null,
                NetTxBytesPerSec = usage.NetTxBytesPerSec is { } tx ? Rate(tx) : null,
            })
            .ToList(),
        TopProcesses = sample.TopProcesses?
            .Take(AgentLimits.MaxTopProcesses)
            .Select(process => new ProcessMetrics
            {
                Pid = process.Pid,
                Name = Clip(process.Name, NameLength),
                CpuPercent = Percent(process.CpuPercent),
                MemoryBytes = Math.Max(0, process.MemoryBytes),
            })
            .ToList(),
        // One entry per service: the database keys failures by host and service name.
        FailedServices = sample.FailedServices?
            .Where(service => !string.IsNullOrWhiteSpace(service.Name))
            .DistinctBy(service => service.Name)
            .Take(AgentLimits.MaxFailedServices)
            .Select(service => new ServiceProblem
            {
                Name = Clip(service.Name, NameLength),
                Description = ClipOptional(service.Description, NameLength),
                State = Clip(string.IsNullOrWhiteSpace(service.State) ? "failed" : service.State, StateLength),
            })
            .ToList(),
    };

    /// <summary>Containers named once each, with Docker's words for states kept to the ones Argus knows.</summary>
    private static ContainerReport Clean(ContainerReport report) => new()
    {
        EngineVersion = ClipOptional(report.EngineVersion, FsTypeLength),
        Problem = ClipOptional(report.Problem, ProblemLength),
        ActionsEnabled = report.ActionsEnabled,
        Items = report.Items
            .Where(container => !string.IsNullOrWhiteSpace(container.Name) && !string.IsNullOrWhiteSpace(container.Id))
            .DistinctBy(container => container.Name)
            .Take(AgentLimits.MaxContainers)
            .Select(container => container with
            {
                Id = Clip(container.Id, FsTypeLength * 2),
                Name = Clip(container.Name, NameLength),
                Image = Clip(container.Image ?? "", ImageLength),
                State = ContainerStates.Contains(container.State) ? container.State : "unknown",
                Health = container.Health is { } health && ContainerHealth.Contains(health) ? health : null,
                RestartCount = Math.Max(0, container.RestartCount),
                CreatedAt = container.CreatedAt.ToUniversalTime(),
                StartedAt = container.StartedAt?.ToUniversalTime(),
                FinishedAt = container.FinishedAt?.ToUniversalTime(),
                RestartPolicy = container.RestartPolicy is { } policy && RestartPolicies.Contains(policy) ? policy : null,
                ComposeProject = ClipOptional(container.ComposeProject, NameLength),
                ComposeService = ClipOptional(container.ComposeService, NameLength),
                Ports = container.Ports.Take(AgentLimits.MaxContainerPorts).Select(port => Clip(port, PortLength)).ToList(),
            })
            .ToList(),
    };

    /// <summary>Guests listed once each, with Proxmox VE's words for types and states kept to the ones Argus knows.</summary>
    private static ProxmoxReport Clean(ProxmoxReport report) => new()
    {
        Node = ClipOptional(report.Node, NameLength),
        Version = ClipOptional(report.Version, FsTypeLength),
        Problem = ClipOptional(report.Problem, ProblemLength),
        ActionsEnabled = report.ActionsEnabled,
        Guests = report.Guests
            .Where(guest => guest.Id > 0 && GuestTypes.Contains(guest.Type))
            .DistinctBy(guest => guest.Id)
            .Take(AgentLimits.MaxProxmoxGuests)
            .Select(guest => guest with
            {
                Name = Clip(string.IsNullOrWhiteSpace(guest.Name) ? $"{guest.Id}" : guest.Name.Trim(), NameLength),
                Status = GuestStatuses.Contains(guest.Status) ? guest.Status : "unknown",
                Lock = ClipOptional(guest.Lock, StateLength),
                UptimeSeconds = Math.Max(0, guest.UptimeSeconds),
                CpuPercent = Percent(Finite(guest.CpuPercent)),
                Cpus = Finite(guest.Cpus) is > 0 and var cpus ? cpus : null,
                MemoryBytes = guest.MemoryBytes is >= 0 and var memory ? memory : null,
                MaxMemoryBytes = guest.MaxMemoryBytes is > 0 and var maxMemory ? maxMemory : null,
                MaxDiskBytes = guest.MaxDiskBytes is > 0 and var maxDisk ? maxDisk : null,
                Tags = guest.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Take(AgentLimits.MaxProxmoxTags).Select(tag => Clip(tag.Trim(), StateLength * 2)).ToList(),
            })
            .ToList(),
    };

    private static double Percent(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;

    private static double? Percent(double? value) => value is { } v ? Percent(v) : null;

    private static double? Finite(double? value) => value is { } v && double.IsFinite(v) ? v : null;

    private static double Rate(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;

    private static string Clip(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

    private static string? ClipOptional(string? value, int maxLength) => value is null ? null : Clip(value, maxLength);
}
