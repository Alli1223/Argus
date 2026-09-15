using Argus.Contracts.Agent;

namespace Argus.Agent.Collection;

/// <summary>Assembles complete samples from the platform's metrics source and the process collector.</summary>
internal sealed class SampleCollector(ISystemMetricsSource system, ProcessCollector processes, TimeProvider time)
{
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

        return new MetricSample
        {
            Timestamp = time.GetUtcNow(),
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
        };
    }
}
