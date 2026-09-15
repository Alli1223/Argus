using Argus.Contracts.Agent;

namespace Argus.Agent.Collection;

/// <summary>Assembles complete samples from the platform's metrics source, the process collector and service checks.</summary>
internal sealed class SampleCollector(
    ISystemMetricsSource system, ProcessCollector processes, IServiceStatusSource services, TimeProvider time)
{
    /// <summary>Service checks can start a process, so they run at most this often.</summary>
    public static readonly TimeSpan ServiceCheckInterval = TimeSpan.FromMinutes(1);

    private DateTimeOffset _nextServiceCheck = DateTimeOffset.MinValue;

    public int TopProcessCount { get; set; } = 10;

    public void Prime()
    {
        system.Prime();
        processes.Prime();
    }

    public MetricSample Collect()
    {
        var reading = system.Collect();
        var (processCount, topProcesses) = processes.Collect(TopProcessCount);
        var now = time.GetUtcNow();

        IReadOnlyList<ServiceProblem>? failedServices = null;
        if (now >= _nextServiceCheck)
        {
            _nextServiceCheck = now + ServiceCheckInterval;
            failedServices = services.Collect();
        }

        return new MetricSample
        {
            Timestamp = now,
            Cpu = reading.Cpu,
            Memory = reading.Memory,
            Load = reading.Load,
            DiskIo = reading.DiskIo,
            Network = reading.Network,
            ProcessCount = processCount,
            UptimeSeconds = reading.UptimeSeconds,
            Filesystems = reading.Filesystems,
            Interfaces = reading.Interfaces,
            TopProcesses = TopProcessCount > 0 ? topProcesses : null,
            FailedServices = failedServices,
        };
    }
}
