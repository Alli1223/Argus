namespace Argus.Server.Features.Summary;

/// <summary>A calendar period as the database worked it out, numbered from 0 (the current one) back.</summary>
public sealed record PeriodBounds(int Index, DateTimeOffset Start, DateTimeOffset End);

/// <summary>One host's hourly rollups added up over one period.</summary>
public sealed record HostPeriodRow(
    int Period,
    Guid HostId,
    double Samples,
    double ReportingSeconds,
    double? DiskReadBytes,
    double? DiskWriteBytes,
    double? NetworkReceivedBytes,
    double? NetworkSentBytes,
    double? CpuAveragePercent,
    double? CpuPeakPercent,
    double? MemoryAveragePercent,
    double? MemoryPeakPercent);

public sealed record HostPeriodTemperature(int Period, Guid HostId, double? PeakCelsius);

public sealed record HostPeriodAlerts(int Period, Guid HostId, int Alerts, int CriticalAlerts);

public static class SummaryMerge
{
    /// <summary>
    /// Puts each host's figures under its period and adds them up for the fleet. Hosts that neither
    /// reported nor alerted in a period are left out of it.
    /// </summary>
    /// <param name="added">When each visible host was added, so availability does not count time before it.</param>
    public static List<UsagePeriod> Merge(
        IReadOnlyList<PeriodBounds> periods,
        IReadOnlyDictionary<Guid, DateTimeOffset> added,
        IEnumerable<HostPeriodRow> metrics,
        IEnumerable<HostPeriodTemperature> temperatures,
        IEnumerable<HostPeriodAlerts> alerts,
        DateTimeOffset now)
    {
        var metricsBy = metrics.ToDictionary(row => (row.Period, row.HostId));
        var temperaturesBy = temperatures.ToDictionary(row => (row.Period, row.HostId));
        var alertsBy = alerts.ToDictionary(row => (row.Period, row.HostId));

        return periods
            .OrderBy(period => period.Index)
            .Select(period =>
            {
                var hosts = metricsBy.Keys.Concat(temperaturesBy.Keys).Concat(alertsBy.Keys)
                    .Where(key => key.Period == period.Index && added.ContainsKey(key.HostId))
                    .Select(key => key.HostId)
                    .Distinct()
                    .Select(hostId =>
                    {
                        var row = metricsBy.GetValueOrDefault((period.Index, hostId));
                        return (Row: row, Usage: new HostUsage(hostId, HostStats(
                            row,
                            temperaturesBy.GetValueOrDefault((period.Index, hostId)),
                            alertsBy.GetValueOrDefault((period.Index, hostId)),
                            Elapsed(period, added[hostId], now))));
                    })
                    .ToList();

                return new UsagePeriod(
                    period.Start,
                    period.End,
                    Totals(hosts),
                    hosts.Select(host => host.Usage).ToList());
            })
            .ToList();
    }

    /// <summary>How much of the period has passed since the host was added.</summary>
    private static TimeSpan Elapsed(PeriodBounds period, DateTimeOffset added, DateTimeOffset now)
    {
        var from = period.Start > added ? period.Start : added;
        var to = period.End < now ? period.End : now;
        return to > from ? to - from : TimeSpan.Zero;
    }

    private static UsageStats HostStats(
        HostPeriodRow? row, HostPeriodTemperature? temperature, HostPeriodAlerts? alerts, TimeSpan elapsed)
    {
        double? availability = row is null || elapsed <= TimeSpan.Zero
            ? null
            : Math.Min(100, 100 * row.ReportingSeconds / elapsed.TotalSeconds);

        return new UsageStats(
            row?.DiskReadBytes,
            row?.DiskWriteBytes,
            row?.NetworkReceivedBytes,
            row?.NetworkSentBytes,
            row?.CpuAveragePercent,
            row?.CpuPeakPercent,
            row?.MemoryAveragePercent,
            row?.MemoryPeakPercent,
            row?.ReportingSeconds,
            availability,
            temperature?.PeakCelsius,
            alerts?.Alerts ?? 0,
            alerts?.CriticalAlerts ?? 0);
    }

    /// <summary>Byte counts and alerts add up; averages are weighted by readings; peaks are the highest.</summary>
    private static UsageStats Totals(List<(HostPeriodRow? Row, HostUsage Usage)> hosts)
    {
        var rows = hosts.Select(host => host.Row).OfType<HostPeriodRow>().ToList();
        var stats = hosts.Select(host => host.Usage.Stats).ToList();

        double? Sum(Func<UsageStats, double?> value)
        {
            var present = stats.Select(value).OfType<double>().ToList();
            return present.Count == 0 ? null : present.Sum();
        }

        double? Max(Func<UsageStats, double?> value)
        {
            var present = stats.Select(value).OfType<double>().ToList();
            return present.Count == 0 ? null : present.Max();
        }

        double? Weighted(Func<HostPeriodRow, double?> value)
        {
            var present = rows.Where(row => value(row) is not null && row.Samples > 0).ToList();
            var samples = present.Sum(row => row.Samples);
            return samples == 0 ? null : present.Sum(row => value(row)!.Value * row.Samples) / samples;
        }

        var availability = stats.Select(stat => stat.AvailabilityPercent).OfType<double>().ToList();

        return new UsageStats(
            Sum(stat => stat.DiskReadBytes),
            Sum(stat => stat.DiskWriteBytes),
            Sum(stat => stat.NetworkReceivedBytes),
            Sum(stat => stat.NetworkSentBytes),
            Weighted(row => row.CpuAveragePercent),
            Max(stat => stat.CpuPeakPercent),
            Weighted(row => row.MemoryAveragePercent),
            Max(stat => stat.MemoryPeakPercent),
            Sum(stat => stat.ReportingSeconds),
            availability.Count == 0 ? null : availability.Average(),
            Max(stat => stat.TemperaturePeakCelsius),
            stats.Sum(stat => stat.Alerts),
            stats.Sum(stat => stat.CriticalAlerts));
    }
}
