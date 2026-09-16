using Argus.Server.Data;
using Argus.Server.Features.Agents;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Argus.Server.Features.Alerts;

public sealed record AlertEvaluationResult(bool Ran, int Fired, int Resolved);

/// <summary>
/// Checks every enabled rule against recent data and opens or resolves alerts. Rules only cover
/// hosts owned by the rule's owner. Each rule's queries run on their own pooled connection, so one
/// failing rule cannot stop the others.
/// </summary>
public sealed class AlertEvaluator(
    ArgusDbContext db,
    NpgsqlDataSource dataSource,
    IOptions<AgentOptions> agents,
    IEnumerable<IAlertEventSink> sinks,
    TimeProvider time,
    ILogger<AlertEvaluator> logger)
{
    /// <summary>Session advisory lock so only one evaluation runs at a time, even with several servers.</summary>
    private const long EvaluationLockKey = 0x4152_4755_5303;

    private sealed record HostInfo(Guid Id, Guid OwnerId, string DisplayName, List<string> Tags, DateTimeOffset? LastSeenAt);

    private sealed record Observation(HostInfo Host, string ResourceKey, Verdict Verdict);

    public async Task<AlertEvaluationResult> EvaluateAsync(CancellationToken cancellationToken)
    {
        await using var lockConnection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (!await lockConnection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT pg_try_advisory_lock(@key)", new { key = EvaluationLockKey }, cancellationToken: cancellationToken)))
        {
            return new AlertEvaluationResult(Ran: false, Fired: 0, Resolved: 0);
        }

        try
        {
            return await EvaluateLockedAsync(cancellationToken);
        }
        finally
        {
            await lockConnection.ExecuteAsync(new CommandDefinition(
                "SELECT pg_advisory_unlock(@key)", new { key = EvaluationLockKey }, cancellationToken: CancellationToken.None));
        }
    }

    private async Task<AlertEvaluationResult> EvaluateLockedAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        // Samples arrive every collection interval; allow two intervals of slack before data counts as stale.
        var tolerance = TimeSpan.FromSeconds(agents.Value.CollectionIntervalSeconds * 2);

        var rules = await db.AlertRules.AsNoTracking().Where(rule => rule.Enabled).ToListAsync(cancellationToken);
        var hostsByOwner = (await db.Hosts.AsNoTracking()
                .Select(host => new HostInfo(host.Id, host.OwnerId, host.DisplayName, host.Tags, host.LastSeenAt))
                .ToListAsync(cancellationToken))
            .ToLookup(host => host.OwnerId);
        var open = await db.Alerts.Where(alert => alert.Status == AlertStatus.Firing).ToListAsync(cancellationToken);
        var openByKey = open
            .Where(alert => alert.RuleId is not null)
            .ToDictionary(alert => (RuleId: alert.RuleId!.Value, alert.HostId, alert.ResourceKey));

        var events = new List<AlertEvent>();
        foreach (var rule in rules)
        {
            try
            {
                var inScope = hostsByOwner[rule.OwnerId].Where(host => InScope(rule, host)).ToList();
                var observed = new HashSet<(Guid HostId, string ResourceKey)>();
                foreach (var (host, resourceKey, verdict) in await ObserveAsync(rule, inScope, now, tolerance, cancellationToken))
                {
                    observed.Add((host.Id, resourceKey));
                    openByKey.TryGetValue((rule.Id, host.Id, resourceKey), out var alert);
                    if (alert is null && verdict.Fire)
                    {
                        alert = Open(rule, host, resourceKey, verdict, now);
                        openByKey[(rule.Id, host.Id, resourceKey)] = alert;
                        events.Add(AlertEvent.From(AlertEventKind.Fired, alert, now));
                    }
                    else if (alert is { Status: AlertStatus.Firing } && verdict.Clear)
                    {
                        Resolve(alert, now, events);
                    }
                    else if (alert is { Status: AlertStatus.Firing } && verdict.Value is { } value)
                    {
                        alert.Value = value;
                        alert.Baseline = verdict.Baseline ?? alert.Baseline;
                    }
                }

                // Hosts that left the rule's scope (a tag was removed, the rule was narrowed) no longer count.
                var scopeIds = inScope.Select(host => host.Id).ToHashSet();
                foreach (var alert in open.Where(alert => alert.RuleId == rule.Id && !scopeIds.Contains(alert.HostId)))
                {
                    Resolve(alert, now, events);
                }

                // A recovered service simply stops being reported as failed, so its alert ends here. (For
                // measured metrics a missing observation only means no data, which keeps the state.)
                if (rule.Metric == AlertMetric.ServiceFailed)
                {
                    foreach (var alert in open.Where(alert => alert.RuleId == rule.Id
                                 && scopeIds.Contains(alert.HostId)
                                 && !observed.Contains((alert.HostId, alert.ResourceKey))))
                    {
                        Resolve(alert, now, events);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Evaluating alert rule {RuleId} ({RuleName}) failed", rule.Id, rule.Name);
            }
        }

        // Alerts whose rule was disabled or deleted end here.
        var enabledRuleIds = rules.Select(rule => rule.Id).ToHashSet();
        foreach (var alert in open.Where(alert => alert.RuleId is not { } ruleId || !enabledRuleIds.Contains(ruleId)))
        {
            Resolve(alert, now, events);
        }

        await db.SaveChangesAsync(cancellationToken);
        await PublishAsync(events, cancellationToken);

        return new AlertEvaluationResult(
            Ran: true,
            Fired: events.Count(e => e.Kind == AlertEventKind.Fired),
            Resolved: events.Count(e => e.Kind == AlertEventKind.Resolved));
    }

    private Alert Open(AlertRule rule, HostInfo host, string resourceKey, Verdict verdict, DateTimeOffset now)
    {
        var alert = new Alert
        {
            RuleId = rule.Id,
            HostId = host.Id,
            OwnerId = rule.OwnerId,
            ResourceKey = resourceKey,
            Title = AlertDecision.Title(rule, host.DisplayName, resourceKey),
            Metric = rule.Metric,
            Condition = rule.Condition,
            Operator = rule.Operator,
            Threshold = rule.Threshold,
            Severity = rule.Severity,
            Status = AlertStatus.Firing,
            Value = verdict.Value,
            Baseline = verdict.Baseline,
            FiredAt = now,
        };
        db.Alerts.Add(alert);
        return alert;
    }

    private static void Resolve(Alert alert, DateTimeOffset now, List<AlertEvent> events)
    {
        if (alert.Status != AlertStatus.Firing)
        {
            return;
        }

        alert.Status = AlertStatus.Resolved;
        alert.ResolvedAt = now;
        events.Add(AlertEvent.From(AlertEventKind.Resolved, alert, now));
    }

    private static bool InScope(AlertRule rule, HostInfo host) =>
        (rule.HostId is null || rule.HostId == host.Id)
        && (rule.Tag is null || host.Tags.Contains(rule.Tag));

    private async Task<List<Observation>> ObserveAsync(
        AlertRule rule, List<HostInfo> hosts, DateTimeOffset now, TimeSpan tolerance, CancellationToken cancellationToken)
    {
        if (hosts.Count == 0)
        {
            return [];
        }

        var duration = TimeSpan.FromSeconds(rule.DurationSeconds);
        if (rule.Metric == AlertMetric.HostOffline)
        {
            return hosts.Select(host => new Observation(host, "", AlertDecision.EvaluateOffline(host.LastSeenAt, duration, now))).ToList();
        }

        if (rule.Metric == AlertMetric.ServiceFailed)
        {
            return await ObserveServicesAsync(rule, hosts, duration, now, cancellationToken);
        }

        var anomaly = rule.Condition == AlertCondition.Anomaly;
        if (anomaly && duration < AlertDecision.MinimumAnomalyDuration)
        {
            duration = AlertDecision.MinimumAnomalyDuration;
        }

        var byId = hosts.ToDictionary(host => host.Id);
        var windowStart = now - AlertDecision.Window(duration, tolerance);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<StatsRow>(new CommandDefinition(
            StatsSql(rule.Metric),
            new
            {
                host_ids = byId.Keys.ToArray(),
                window_start = windowStart,
                recent_start = now - AlertDecision.ResolveWindow(duration, tolerance),
                now,
                resource = string.IsNullOrWhiteSpace(rule.ResourceFilter) ? null : rule.ResourceFilter,
            },
            cancellationToken: cancellationToken));

        Dictionary<Guid, BaselineStats> baselines = anomaly
            ? await BaselinesAsync(connection, rule.Metric, byId.Keys.ToArray(), windowStart, cancellationToken)
            : [];

        return rows
            .Where(row => byId.ContainsKey(row.HostId))
            .Select(row => new Observation(
                byId[row.HostId],
                row.ResourceKey,
                anomaly
                    ? AlertDecision.EvaluateAnomaly(
                        rule.Operator, rule.Threshold, rule.Metric, duration, row.ToStats(), baselines.GetValueOrDefault(row.HostId), now, tolerance)
                    : AlertDecision.Evaluate(rule.Operator, rule.Threshold, duration, row.ToStats(), now, tolerance)))
            .ToList();
    }

    /// <summary>
    /// Each host's usual level of a metric: the mean and spread of its 5-minute averages over the
    /// baseline period. Only buckets that end before the rule's window count, so the stretch being
    /// judged is never part of what it is judged against.
    /// </summary>
    private static async Task<Dictionary<Guid, BaselineStats>> BaselinesAsync(
        NpgsqlConnection connection, AlertMetric metric, Guid[] hostIds, DateTimeOffset windowStart, CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<BaselineRow>(new CommandDefinition($"""
            SELECT host_id, count(value)::int AS buckets, avg(value) AS mean, stddev_samp(value) AS std_dev
            FROM (
                SELECT m.host_id, {RollupValueExpression(metric)} AS value
                FROM host_metrics_5m m
                JOIN hosts h ON h.id = m.host_id
                WHERE m.host_id = ANY(@host_ids) AND m.bucket >= @baseline_start AND m.bucket <= @last_bucket
            ) b
            GROUP BY host_id
            """,
            new
            {
                host_ids = hostIds,
                baseline_start = windowStart - AlertDecision.BaselinePeriod,
                last_bucket = windowStart - RollupBucket,
            },
            cancellationToken: cancellationToken));

        return rows.ToDictionary(row => row.HostId, row => new BaselineStats(row.Buckets, row.Mean ?? 0, row.StdDev ?? 0));
    }

    private static readonly TimeSpan RollupBucket = TimeSpan.FromMinutes(5);

    /// <summary>The metric from the 5-minute rollup, matching <see cref="ValueExpression"/>.</summary>
    private static string RollupValueExpression(AlertMetric metric) => metric switch
    {
        AlertMetric.CpuUsage => "m.cpu_usage_pct",
        AlertMetric.MemoryUsage => "m.mem_used_bytes / NULLIF(m.mem_total_bytes, 0) * 100",
        AlertMetric.SwapUsage => "m.swap_used_bytes / NULLIF(m.swap_total_bytes, 0) * 100",
        AlertMetric.LoadPerCore => "m.load_1 / NULLIF(h.cpu_logical_processors, 0)",
        AlertMetric.DiskIoUtilization => "m.disk_util_pct",
        AlertMetric.NetworkReceive => "m.net_rx_bps",
        AlertMetric.NetworkTransmit => "m.net_tx_bps",
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "No 5-minute rollup for this metric."),
    };

    /// <summary>Services failing in each host's newest check, each with the time it was first seen failing.</summary>
    private async Task<List<Observation>> ObserveServicesAsync(
        AlertRule rule, List<HostInfo> hosts, TimeSpan duration, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var byId = hosts.ToDictionary(host => host.Id);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ServiceFailureRow>(new CommandDefinition("""
            SELECT f.host_id, f.service, f.since
            FROM host_service_failures f
            JOIN host_service_checks c ON c.host_id = f.host_id AND c.checked_at = f.last_seen
            WHERE f.host_id = ANY(@host_ids) AND (@resource::text IS NULL OR f.service = @resource)
            """,
            new
            {
                host_ids = byId.Keys.ToArray(),
                resource = string.IsNullOrWhiteSpace(rule.ResourceFilter) ? null : rule.ResourceFilter,
            },
            cancellationToken: cancellationToken));

        return rows
            .Where(row => byId.ContainsKey(row.HostId))
            .Select(row => new Observation(
                byId[row.HostId], row.Service, AlertDecision.EvaluateServiceFailure(row.SinceUtc, duration, now)))
            .ToList();
    }

    /// <summary>Window aggregates per host (and mount point). The value expressions are fixed per metric.</summary>
    private static string StatsSql(AlertMetric metric)
    {
        var source = metric.IsPerFilesystem()
            ? $"""
               SELECT f.host_id, f.mount_point AS resource_key, f.time, {ValueExpression(metric)} AS value
               FROM filesystem_metrics f
               WHERE f.host_id = ANY(@host_ids) AND f.time > @window_start AND f.time <= @now
                 AND (@resource::text IS NULL OR f.mount_point = @resource)
               """
            : $"""
               SELECT m.host_id, ''::text AS resource_key, m.time, {ValueExpression(metric)} AS value
               FROM host_metrics m
               JOIN hosts h ON h.id = m.host_id
               WHERE m.host_id = ANY(@host_ids) AND m.time > @window_start AND m.time <= @now
               """;

        return $"""
            SELECT host_id, resource_key,
                   count(value)::int                                        AS samples,
                   min(time) FILTER (WHERE value IS NOT NULL)               AS first_time,
                   max(time) FILTER (WHERE value IS NOT NULL)               AS last_time,
                   min(value)                                               AS min_value,
                   max(value)                                               AS max_value,
                   avg(value)                                               AS average,
                   (count(value) FILTER (WHERE time > @recent_start))::int  AS recent_samples,
                   min(value) FILTER (WHERE time > @recent_start)           AS recent_min,
                   max(value) FILTER (WHERE time > @recent_start)           AS recent_max
            FROM ({source}) s
            GROUP BY host_id, resource_key
            """;
    }

    private static string ValueExpression(AlertMetric metric) => metric switch
    {
        AlertMetric.CpuUsage => "m.cpu_usage_pct::float8",
        AlertMetric.MemoryUsage => "m.mem_used_bytes::float8 / NULLIF(m.mem_total_bytes, 0) * 100",
        AlertMetric.SwapUsage => "m.swap_used_bytes::float8 / NULLIF(m.swap_total_bytes, 0) * 100",
        AlertMetric.LoadPerCore => "m.load_1::float8 / NULLIF(h.cpu_logical_processors, 0)",
        AlertMetric.DiskIoUtilization => "m.disk_util_pct::float8",
        AlertMetric.NetworkReceive => "m.net_rx_bps",
        AlertMetric.NetworkTransmit => "m.net_tx_bps",
        AlertMetric.DiskUsage => "f.used_bytes::float8 / NULLIF(f.used_bytes + f.available_bytes, 0) * 100",
        AlertMetric.InodeUsage => "f.inodes_used::float8 / NULLIF(f.inodes_total, 0) * 100",
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "Not a sampled metric."),
    };

    private async Task PublishAsync(List<AlertEvent> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return;
        }

        foreach (var sink in sinks)
        {
            try
            {
                await sink.PublishAsync(events, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Alert event sink {Sink} failed", sink.GetType().Name);
            }
        }
    }

    private sealed class StatsRow
    {
        public Guid HostId { get; set; }
        public string ResourceKey { get; set; } = "";
        public int Samples { get; set; }
        public DateTime? FirstTime { get; set; }
        public DateTime? LastTime { get; set; }
        public double? MinValue { get; set; }
        public double? MaxValue { get; set; }
        public double? Average { get; set; }
        public int RecentSamples { get; set; }
        public double? RecentMin { get; set; }
        public double? RecentMax { get; set; }

        public WindowStats ToStats() => new(
            Samples, Utc(FirstTime), Utc(LastTime), MinValue, MaxValue, Average, RecentSamples, RecentMin, RecentMax);

        private static DateTimeOffset? Utc(DateTime? value) =>
            value is { } utc ? new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)) : null;
    }

    private sealed class BaselineRow
    {
        public Guid HostId { get; set; }
        public int Buckets { get; set; }
        public double? Mean { get; set; }
        public double? StdDev { get; set; }
    }

    private sealed class ServiceFailureRow
    {
        public Guid HostId { get; set; }
        public string Service { get; set; } = "";
        public DateTime Since { get; set; }

        public DateTimeOffset SinceUtc => new(DateTime.SpecifyKind(Since, DateTimeKind.Utc));
    }
}
