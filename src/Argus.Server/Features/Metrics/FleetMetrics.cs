namespace Argus.Server.Features.Metrics;

/// <summary>
/// Every visible host's history merged into one series. <see cref="Totals"/> holds CPU, memory and load
/// averaged across the hosts reporting in each bucket, traffic and disk I/O summed across them, and how
/// many hosts reported; <see cref="Hosts"/> holds each host's own CPU on the same time axis.
/// </summary>
public sealed record FleetMetrics(MetricSeries Totals, IReadOnlyList<FleetHostCpu> Hosts);

public sealed record FleetHostCpu(Guid HostId, string DisplayName, double?[] Cpu);

/// <summary>One host's readings in one bucket, as the fleet query returns them.</summary>
public sealed record FleetRow(
    DateTime Bucket,
    Guid HostId,
    double? Cpu,
    double? Memory,
    double? Load1,
    double? NetRx,
    double? NetTx,
    double? DiskRead,
    double? DiskWrite);

public static class FleetMerge
{
    /// <summary>
    /// Merges per-host buckets. A bucket where no host reported stays empty (null) rather than zero, so
    /// charts show a gap. Hosts with no readings at all are left out of <see cref="FleetMetrics.Hosts"/>.
    /// </summary>
    public static FleetMetrics Merge(
        IEnumerable<FleetRow> rows,
        IReadOnlyDictionary<Guid, string> names,
        SeriesRange range,
        SeriesSource source,
        TimeSpan bucket)
    {
        var all = rows.ToList();
        var times = all.Select(row => row.Bucket).Distinct().Order().ToList();
        var index = times.Select((time, position) => (time, position)).ToDictionary(pair => pair.time, pair => pair.position);
        var buckets = all.ToLookup(row => row.Bucket);

        double?[] Column(Func<IEnumerable<FleetRow>, double?> combine) =>
            [.. times.Select(time => combine(buckets[time]))];

        var series = new Dictionary<string, double?[]>(StringComparer.Ordinal)
        {
            ["cpu"] = Column(rows => Average(rows.Select(row => row.Cpu))),
            ["memory"] = Column(rows => Average(rows.Select(row => row.Memory))),
            ["load1"] = Column(rows => Average(rows.Select(row => row.Load1))),
            ["netRx"] = Column(rows => Sum(rows.Select(row => row.NetRx))),
            ["netTx"] = Column(rows => Sum(rows.Select(row => row.NetTx))),
            ["diskRead"] = Column(rows => Sum(rows.Select(row => row.DiskRead))),
            ["diskWrite"] = Column(rows => Sum(rows.Select(row => row.DiskWrite))),
            ["hosts"] = Column(rows => rows.Count(row => row.Cpu is not null)),
        };

        var hosts = all
            .Where(row => row.Cpu is not null)
            .Select(row => row.HostId)
            .Distinct()
            .Select(hostId =>
            {
                var cpu = new double?[times.Count];
                foreach (var row in all.Where(row => row.HostId == hostId))
                {
                    cpu[index[row.Bucket]] = row.Cpu;
                }

                return new FleetHostCpu(hostId, names.GetValueOrDefault(hostId, ""), cpu);
            })
            .OrderBy(host => host.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var time = times
            .Select(utc => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds())
            .ToList();
        return new FleetMetrics(
            new MetricSeries(range.From, range.To, source.Label(), (int)bucket.TotalSeconds, time, series),
            hosts);
    }

    private static double? Average(IEnumerable<double?> values)
    {
        var present = values.OfType<double>().ToList();
        return present.Count == 0 ? null : present.Average();
    }

    private static double? Sum(IEnumerable<double?> values)
    {
        var present = values.OfType<double>().ToList();
        return present.Count == 0 ? null : present.Sum();
    }
}
