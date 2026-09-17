using Argus.Contracts.Agent;
using Argus.Server.Features.Metrics;
using Dapper;
using Npgsql;

namespace Argus.Server.Features.Containers;

/// <summary>
/// Hosts' containers: the newest report of each host (host_containers), what changed between reports
/// (container_events) and what running containers used (container_metrics, written by the metrics
/// ingestor). Callers check that the user may see a host before asking; these queries only filter by id.
/// </summary>
public sealed class ContainerStore(NpgsqlDataSource dataSource)
{
    private const string SummaryColumns = """
        c.name, c.container_id AS id, c.image, c.state, c.health, c.restart_count, c.exit_code, c.oom_killed,
        c.created_at, c.started_at, c.finished_at, c.state_since, c.restart_policy, c.compose_project,
        c.compose_service, c.ports, c.host_id,
        COALESCE(r.restarts, 0) AS restarts_last_hour,
        u.time AS usage_time, u.cpu_pct, u.mem_used_bytes, u.mem_limit_bytes, u.net_rx_bps, u.net_tx_bps
        """;

    private const string SummaryJoins = """
        LEFT JOIN LATERAL (
            SELECT time, cpu_pct::float8 AS cpu_pct, mem_used_bytes, mem_limit_bytes, net_rx_bps, net_tx_bps
            FROM container_metrics m
            WHERE m.host_id = c.host_id AND m.container = c.name AND m.time > now() - interval '2 minutes' AND c.state = 'running'
            ORDER BY m.time DESC
            LIMIT 1) u ON true
        LEFT JOIN LATERAL (
            SELECT sum(e.count)::int AS restarts
            FROM container_events e
            WHERE e.host_id = c.host_id AND e.container = c.name AND e.kind = 'restarted' AND e.time > now() - interval '1 hour') r ON true
        """;

    /// <summary>
    /// Applies a host's report of its containers, unless a newer one is already stored. When the agent
    /// could not read Docker, the containers last seen are kept as they were.
    /// </summary>
    public async Task ApplyReportAsync(Guid hostId, DateTimeOffset checkedAt, ContainerReport report, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var host = new { host_id = hostId };

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(hashtextextended('containers:' || @host_id::text, 0))", host, transaction, cancellationToken: cancellationToken));
        var last = await connection.QuerySingleOrDefaultAsync<DateTime?>(new CommandDefinition(
            "SELECT checked_at FROM host_container_checks WHERE host_id = @host_id", host, transaction, cancellationToken: cancellationToken));
        if (last is { } previousCheck && Utc(previousCheck) >= checkedAt)
        {
            return;
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO host_container_checks (host_id, checked_at, engine_version, problem, actions_enabled)
            VALUES (@host_id, @checked_at, @engine_version, @problem, @actions_enabled)
            ON CONFLICT (host_id) DO UPDATE SET checked_at = excluded.checked_at, engine_version = excluded.engine_version,
                problem = excluded.problem, actions_enabled = excluded.actions_enabled
            """,
            new
            {
                host_id = hostId,
                checked_at = checkedAt,
                engine_version = report.EngineVersion,
                problem = report.Problem,
                actions_enabled = report.ActionsEnabled,
            },
            transaction,
            cancellationToken: cancellationToken));

        if (report.Problem is null)
        {
            var stored = (await connection.QueryAsync<ContainerRow>(new CommandDefinition(
                    "SELECT c.*, c.container_id AS id FROM host_containers c WHERE c.host_id = @host_id", host, transaction, cancellationToken: cancellationToken)))
                .Select(row => new StoredContainer(row.ToInfo(), Utc(row.StateSince)))
                .ToList();
            var changes = ContainerChanges.Compare(stored, report.Items, checkedAt, firstReport: last is null);
            await SaveAsync(connection, transaction, hostId, changes, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<HostContainers> GetHostAsync(Guid hostId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var check = await connection.QuerySingleOrDefaultAsync<CheckRow>(new CommandDefinition(
            "SELECT host_id, checked_at, engine_version, problem, actions_enabled FROM host_container_checks WHERE host_id = @host_id",
            new { host_id = hostId }, cancellationToken: cancellationToken));
        if (check is null)
        {
            return new HostContainers(null, null, null, false, []);
        }

        var containers = await SummariesAsync(connection, [hostId], cancellationToken);
        return new HostContainers(Utc(check.CheckedAt), check.EngineVersion, check.Problem, check.ActionsEnabled, containers.GetValueOrDefault(hostId, []));
    }

    /// <summary>The containers of every given host that has reported any, keyed by host.</summary>
    public async Task<List<(Guid HostId, DateTimeOffset CheckedAt, string? Problem, bool ActionsEnabled, List<ContainerSummary> Containers)>> GetHostsAsync(
        IReadOnlyCollection<Guid> hostIds, CancellationToken cancellationToken)
    {
        if (hostIds.Count == 0)
        {
            return [];
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var checks = await connection.QueryAsync<CheckRow>(new CommandDefinition(
            "SELECT host_id, checked_at, engine_version, problem, actions_enabled FROM host_container_checks WHERE host_id = ANY(@host_ids)",
            new { host_ids = hostIds.ToArray() }, cancellationToken: cancellationToken));
        var containers = await SummariesAsync(connection, hostIds.ToArray(), cancellationToken);

        return checks
            .Select(check => (check.HostId, Utc(check.CheckedAt), check.Problem, check.ActionsEnabled, containers.GetValueOrDefault(check.HostId, [])))
            .ToList();
    }

    /// <summary>One container with its latest events, or null when the host has no container of that name.</summary>
    public async Task<(ContainerSummary Container, bool ActionsEnabled, List<ContainerEventInfo> Events)?> GetContainerAsync(
        Guid hostId, string name, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var summaries = await SummariesAsync(connection, [hostId], cancellationToken, name);
        if (summaries.GetValueOrDefault(hostId)?.SingleOrDefault() is not { } container)
        {
            return null;
        }

        var actions = await connection.QuerySingleOrDefaultAsync<bool>(new CommandDefinition(
            "SELECT actions_enabled FROM host_container_checks WHERE host_id = @host_id", new { host_id = hostId }, cancellationToken: cancellationToken));
        var events = await connection.QueryAsync<EventRow>(new CommandDefinition("""
            SELECT time, kind, detail, count FROM container_events
            WHERE host_id = @host_id AND container = @name
            ORDER BY time DESC
            LIMIT 100
            """, new { host_id = hostId, name }, cancellationToken: cancellationToken));

        return (container, actions, events.Select(row => new ContainerEventInfo(Utc(row.Time), row.Kind, row.Detail, row.Count)).ToList());
    }

    /// <summary>A container's CPU, memory and traffic over time; series cpu, cpuMax, memory, memoryLimit, netRx and netTx.</summary>
    public async Task<MetricSeries> GetHistoryAsync(
        Guid hostId, string name, SeriesRange range, TimeSpan collectionInterval, CancellationToken cancellationToken)
    {
        var (source, bucket) = SeriesResolution.ChooseRawOrHourly(range.To - range.From, range.Points, collectionInterval);
        var sql = source == SeriesSource.Raw
            ? """
              SELECT time_bucket_gapfill(@bucket, time, @from, @to) AS bucket,
                     avg(cpu_pct)::float8 AS cpu, max(cpu_pct)::float8 AS cpu_max, avg(mem_used_bytes)::float8 AS memory,
                     max(mem_limit_bytes)::float8 AS memory_limit, avg(net_rx_bps) AS net_rx, avg(net_tx_bps) AS net_tx
              FROM container_metrics
              WHERE host_id = @host_id AND container = @name AND time >= @from AND time < @to
              GROUP BY 1 ORDER BY 1
              """
            : """
              SELECT time_bucket_gapfill(@bucket, bucket, @from, @to) AS bucket,
                     avg(cpu_pct) AS cpu, max(cpu_pct_max) AS cpu_max, avg(mem_used_bytes) AS memory,
                     max(mem_limit_bytes)::float8 AS memory_limit, avg(net_rx_bps) AS net_rx, avg(net_tx_bps) AS net_tx
              FROM container_metrics_1h
              WHERE host_id = @host_id AND container = @name AND bucket >= @from AND bucket < @to
              GROUP BY 1 ORDER BY 1
              """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<HistoryRow>(new CommandDefinition(
            sql, new { host_id = hostId, name, bucket, from = range.From, to = range.To }, cancellationToken: cancellationToken))).ToList();

        return new MetricSeries(range.From, range.To, source.Label(), (int)bucket.TotalSeconds,
            rows.Select(row => new DateTimeOffset(DateTime.SpecifyKind(row.Bucket, DateTimeKind.Utc)).ToUnixTimeSeconds()).ToList(),
            new Dictionary<string, double?[]>
            {
                ["cpu"] = [.. rows.Select(row => row.Cpu)],
                ["cpuMax"] = [.. rows.Select(row => row.CpuMax)],
                ["memory"] = [.. rows.Select(row => row.Memory)],
                ["memoryLimit"] = [.. rows.Select(row => row.MemoryLimit)],
                ["netRx"] = [.. rows.Select(row => row.NetRx)],
                ["netTx"] = [.. rows.Select(row => row.NetTx)],
            });
    }

    private static async Task SaveAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid hostId, ContainerChangeSet changes, CancellationToken cancellationToken)
    {
        if (changes.Removed.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM host_containers WHERE host_id = @host_id AND name = ANY(@names)",
                new { host_id = hostId, names = changes.Removed.ToArray() }, transaction, cancellationToken: cancellationToken));
        }

        // A report holds at most a few hundred containers and comes at most once a minute, so row by row is fine.
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO host_containers (
                host_id, name, container_id, image, state, health, restart_count, exit_code, oom_killed, created_at,
                started_at, finished_at, restart_policy, compose_project, compose_service, ports, state_since)
            VALUES (
                @host_id, @name, @container_id, @image, @state, @health, @restart_count, @exit_code, @oom_killed, @created_at,
                @started_at, @finished_at, @restart_policy, @compose_project, @compose_service, @ports, @state_since)
            ON CONFLICT (host_id, name) DO UPDATE SET
                container_id = excluded.container_id, image = excluded.image, state = excluded.state, health = excluded.health,
                restart_count = excluded.restart_count, exit_code = excluded.exit_code, oom_killed = excluded.oom_killed,
                created_at = excluded.created_at, started_at = excluded.started_at, finished_at = excluded.finished_at,
                restart_policy = excluded.restart_policy, compose_project = excluded.compose_project,
                compose_service = excluded.compose_service, ports = excluded.ports, state_since = excluded.state_since
            """,
            changes.Current.Select(stored => new
            {
                host_id = hostId,
                name = stored.Info.Name,
                container_id = stored.Info.Id,
                image = stored.Info.Image,
                state = stored.Info.State,
                health = stored.Info.Health,
                restart_count = stored.Info.RestartCount,
                exit_code = stored.Info.ExitCode,
                oom_killed = stored.Info.OomKilled,
                created_at = stored.Info.CreatedAt,
                started_at = stored.Info.StartedAt,
                finished_at = stored.Info.FinishedAt,
                restart_policy = stored.Info.RestartPolicy,
                compose_project = stored.Info.ComposeProject,
                compose_service = stored.Info.ComposeService,
                ports = stored.Info.Ports.ToArray(),
                state_since = stored.StateSince,
            }),
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO container_events (time, host_id, container, kind, detail, count) VALUES (@time, @host_id, @container, @kind, @detail, @count)",
            changes.Events.Select(change => new
            {
                time = change.Time,
                host_id = hostId,
                container = change.Container,
                kind = change.Kind,
                detail = change.Detail,
                count = change.Count,
            }),
            transaction,
            cancellationToken: cancellationToken));
    }

    private static async Task<Dictionary<Guid, List<ContainerSummary>>> SummariesAsync(
        NpgsqlConnection connection, Guid[] hostIds, CancellationToken cancellationToken, string? name = null)
    {
        var rows = await connection.QueryAsync<ContainerRow>(new CommandDefinition($"""
            SELECT {SummaryColumns}
            FROM host_containers c
            {SummaryJoins}
            WHERE c.host_id = ANY(@host_ids) AND (@name::text IS NULL OR c.name = @name)
            ORDER BY c.name
            """, new { host_ids = hostIds, name }, cancellationToken: cancellationToken));

        return rows
            .GroupBy(row => row.HostId)
            .ToDictionary(group => group.Key, group => group.Select(row => row.ToSummary()).ToList());
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? Utc(DateTime? value) => value is { } utc ? Utc(utc) : null;

    private sealed class CheckRow
    {
        public Guid HostId { get; set; }
        public DateTime CheckedAt { get; set; }
        public string? EngineVersion { get; set; }
        public string? Problem { get; set; }
        public bool ActionsEnabled { get; set; }
    }

    private sealed class ContainerRow
    {
        public Guid HostId { get; set; }
        public string Name { get; set; } = "";
        public string Id { get; set; } = "";
        public string Image { get; set; } = "";
        public string State { get; set; } = "";
        public string? Health { get; set; }
        public int RestartCount { get; set; }
        public int RestartsLastHour { get; set; }
        public int? ExitCode { get; set; }
        public bool OomKilled { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public DateTime StateSince { get; set; }
        public string? RestartPolicy { get; set; }
        public string? ComposeProject { get; set; }
        public string? ComposeService { get; set; }
        public string[] Ports { get; set; } = [];
        public DateTime? UsageTime { get; set; }
        public double? CpuPct { get; set; }
        public long? MemUsedBytes { get; set; }
        public long? MemLimitBytes { get; set; }
        public double? NetRxBps { get; set; }
        public double? NetTxBps { get; set; }

        public ContainerInfo ToInfo() => new()
        {
            Id = Id,
            Name = Name,
            Image = Image,
            State = State,
            Health = Health,
            RestartCount = RestartCount,
            ExitCode = ExitCode,
            OomKilled = OomKilled,
            CreatedAt = Utc(CreatedAt),
            StartedAt = Utc(StartedAt),
            FinishedAt = Utc(FinishedAt),
            RestartPolicy = RestartPolicy,
            ComposeProject = ComposeProject,
            ComposeService = ComposeService,
            Ports = Ports,
        };

        public ContainerSummary ToSummary() => new(
            Name, Id, Image, State, Health, RestartCount, RestartsLastHour, ExitCode, OomKilled, Utc(CreatedAt), Utc(StartedAt),
            Utc(FinishedAt), Utc(StateSince), RestartPolicy, ComposeProject, ComposeService, Ports,
            UsageTime is { } time
                ? new ContainerUsageSnapshot(Utc(time), CpuPct ?? 0, MemUsedBytes ?? 0, MemLimitBytes, NetRxBps, NetTxBps)
                : null);
    }

    private sealed class EventRow
    {
        public DateTime Time { get; set; }
        public string Kind { get; set; } = "";
        public string? Detail { get; set; }
        public int Count { get; set; }
    }

    private sealed class HistoryRow
    {
        public DateTime Bucket { get; set; }
        public double? Cpu { get; set; }
        public double? CpuMax { get; set; }
        public double? Memory { get; set; }
        public double? MemoryLimit { get; set; }
        public double? NetRx { get; set; }
        public double? NetTx { get; set; }
    }
}
