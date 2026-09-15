using System.ComponentModel;
using System.Diagnostics;
using Argus.Contracts.Agent;

namespace Argus.Agent.Collection;

/// <summary>
/// Counts processes and finds the busiest ones. CPU usage is the change in each process's CPU time
/// since the previous collection, as a share of the whole machine (all cores), like Task Manager.
/// </summary>
internal sealed class ProcessCollector
{
    private readonly int _processorCount = Environment.ProcessorCount;
    private Dictionary<int, TimeSpan> _cpuTimes = [];
    private long _lastCollection;

    public void Prime() => Collect(0);

    /// <summary>
    /// Returns the process count and the <paramref name="topCount"/> busiest processes by CPU plus the
    /// <paramref name="topCount"/> largest by memory (so memory hogs show up even when idle).
    /// </summary>
    public (int Count, List<ProcessMetrics> Top) Collect(int topCount)
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = _lastCollection == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(_lastCollection, now);
        _lastCollection = now;

        var cpuTimes = new Dictionary<int, TimeSpan>();
        var rows = new List<ProcessMetrics>();
        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes)
            {
                // PID 0 is the Windows "System Idle Process", whose CPU time is idle time.
                if (process.Id == 0)
                {
                    continue;
                }

                try
                {
                    var cpuTime = process.TotalProcessorTime;
                    cpuTimes[process.Id] = cpuTime;

                    var cpuPercent = 0.0;
                    if (elapsed > TimeSpan.Zero && _cpuTimes.TryGetValue(process.Id, out var previous) && cpuTime >= previous)
                    {
                        cpuPercent = (cpuTime - previous).TotalMilliseconds / (elapsed.TotalMilliseconds * _processorCount) * 100;
                    }

                    rows.Add(new ProcessMetrics
                    {
                        Pid = process.Id,
                        Name = process.ProcessName,
                        CpuPercent = Math.Min(100, cpuPercent),
                        MemoryBytes = process.WorkingSet64,
                    });
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
                {
                    // The process exited meanwhile, or the OS will not tell us about it.
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        _cpuTimes = cpuTimes;

        if (topCount <= 0)
        {
            return (processes.Length, []);
        }

        var top = rows.OrderByDescending(p => p.CpuPercent).Take(topCount)
            .Concat(rows.OrderByDescending(p => p.MemoryBytes).Take(topCount))
            .DistinctBy(p => p.Pid)
            .OrderByDescending(p => p.CpuPercent)
            .ThenByDescending(p => p.MemoryBytes)
            .ToList();

        return (processes.Length, top);
    }
}
