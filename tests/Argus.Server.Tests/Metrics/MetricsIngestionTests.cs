using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Argus.Contracts.Agent;
using Argus.Server.Data;
using Argus.Server.Tests.Agents;
using Argus.Server.Tests.Enrollment;
using Argus.Server.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Argus.Server.Tests.Metrics;

public sealed class MetricsIngestionTests(ArgusAppFixture app) : IClassFixture<ArgusAppFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(Guid HostId, HttpClient Agent)> RegisteredAgentAsync(string email)
    {
        await app.CreateUserAsync(email, EnrollmentTokenTests.Password);
        var owner = await app.CreateSignedInClientAsync(email, EnrollmentTokenTests.Password);
        var token = await EnrollmentTokenTests.CreateTokenAsync(owner);

        var response = await app.Factory.CreateClient().PostAsJsonAsync(
            AgentApi.Register, AgentRegistrationTests.Registration(token.Token, "machine-" + email), AgentJsonContext.Default.RegisterAgentRequest, Ct);
        response.EnsureSuccessStatusCode();
        var registered = (await response.Content.ReadFromJsonAsync(AgentJsonContext.Default.RegisterAgentResponse, Ct))!;

        var agent = app.Factory.CreateClient();
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registered.AgentKey);
        return (registered.HostId, agent);
    }

    internal static MetricSample FullSample(DateTimeOffset timestamp) => MetricsBatchValidatorTests.Sample(timestamp, cpu: 42) with
    {
        Load = new LoadMetrics { Load1 = 0.5, Load5 = 0.4, Load15 = 0.3 },
        DiskIo = new DiskIoMetrics { ReadBytesPerSec = 1000, WriteBytesPerSec = 2000, UtilizationPercent = 3 },
        Network = new NetworkMetrics { RxBytesPerSec = 300, TxBytesPerSec = 400 },
        ProcessCount = 120,
        UptimeSeconds = 3600,
        Filesystems =
        [
            new FilesystemMetrics { MountPoint = "/", Device = "/dev/sda1", FsType = "ext4", TotalBytes = 100, UsedBytes = 40, AvailableBytes = 55 },
            new FilesystemMetrics { MountPoint = "/var", TotalBytes = 50, UsedBytes = 10, AvailableBytes = 38 },
        ],
        Interfaces = [new NetworkInterfaceMetrics { Name = "eth0", RxBytesPerSec = 300, TxBytesPerSec = 400, RxPacketsPerSec = 3 }],
        Temperatures =
        [
            new TemperatureMetrics { Device = "coretemp", Sensor = "Package id 0", Celsius = 55.5 },
            new TemperatureMetrics { Device = "nvme0", Sensor = "Composite", Celsius = 41 },
        ],
        TopProcesses = [new ProcessMetrics { Pid = 1, Name = "systemd", CpuPercent = 0.5, MemoryBytes = 1024 }],
    };

    private static Task<HttpResponseMessage> PostAsync(HttpClient agent, params MetricSample[] samples) =>
        agent.PostAsJsonAsync(AgentApi.Metrics, new MetricsBatch { Samples = samples }, AgentJsonContext.Default.MetricsBatch, Ct);

    private static async Task<int> AcceptedAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync(AgentJsonContext.Default.MetricsBatchResponse, Ct))!.Accepted;
    }

    private Task<T> ScalarAsync<T>(string sql, Guid? hostId = null) =>
        app.WithScopeAsync(async services =>
        {
            await using var command = services.GetRequiredService<NpgsqlDataSource>().CreateCommand(sql);
            if (hostId is { } id)
            {
                command.Parameters.AddWithValue("host_id", id);
            }

            return (T)(await command.ExecuteScalarAsync(Ct))!;
        });

    [Fact]
    public async Task Batches_are_stored_in_every_table_exactly_once()
    {
        var (hostId, agent) = await RegisteredAgentAsync("ingest-a@example.com");
        var now = DateTimeOffset.UtcNow;
        MetricSample[] samples = [FullSample(now.AddSeconds(-30)), FullSample(now.AddSeconds(-15)), FullSample(now)];

        Assert.Equal(3, await AcceptedAsync(await PostAsync(agent, samples)));

        // Agents re-send batches when unsure whether they arrived; duplicates are ignored.
        Assert.Equal(0, await AcceptedAsync(await PostAsync(agent, samples)));

        Assert.Equal(3L, await ScalarAsync<long>("SELECT count(*) FROM host_metrics WHERE host_id = @host_id", hostId));
        Assert.Equal(6L, await ScalarAsync<long>("SELECT count(*) FROM filesystem_metrics WHERE host_id = @host_id", hostId));
        Assert.Equal(3L, await ScalarAsync<long>("SELECT count(*) FROM network_metrics WHERE host_id = @host_id", hostId));
        Assert.Equal(6L, await ScalarAsync<long>("SELECT count(*) FROM temperature_metrics WHERE host_id = @host_id", hostId));
        Assert.Equal(42f, await ScalarAsync<float>("SELECT max(cpu_usage_pct) FROM host_metrics WHERE host_id = @host_id", hostId));
        Assert.Equal("systemd", await ScalarAsync<string>("SELECT processes->0->>'name' FROM host_processes WHERE host_id = @host_id", hostId));

        var host = await app.WithScopeAsync(services =>
            services.GetRequiredService<ArgusDbContext>().Hosts.AsNoTracking().SingleAsync(h => h.Id == hostId, Ct));
        Assert.True(host.LastSeenAt >= now.AddSeconds(-5));
    }

    [Fact]
    public async Task Samples_outside_the_time_window_are_skipped()
    {
        var (_, agent) = await RegisteredAgentAsync("ingest-b@example.com");
        var now = DateTimeOffset.UtcNow;

        var accepted = await AcceptedAsync(await PostAsync(agent, FullSample(now.AddDays(-10)), FullSample(now.AddHours(1)), FullSample(now)));

        Assert.Equal(1, accepted);
    }

    [Fact]
    public async Task Gzip_compressed_batches_are_accepted()
    {
        var (_, agent) = await RegisteredAgentAsync("ingest-c@example.com");
        var json = JsonSerializer.SerializeToUtf8Bytes(
            new MetricsBatch { Samples = [FullSample(DateTimeOffset.UtcNow)] }, AgentJsonContext.Default.MetricsBatch);
        using var compressed = new MemoryStream();
        await using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            await gzip.WriteAsync(json, Ct);
        }

        var content = new ByteArrayContent(compressed.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Headers.ContentEncoding.Add("gzip");

        Assert.Equal(1, await AcceptedAsync(await agent.PostAsync(AgentApi.Metrics, content, Ct)));
    }

    [Fact]
    public async Task Invalid_batches_and_unknown_agents_are_rejected()
    {
        var (_, agent) = await RegisteredAgentAsync("ingest-d@example.com");

        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(agent)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(app.Factory.CreateClient(), FullSample(DateTimeOffset.UtcNow))).StatusCode);
    }

    [Fact]
    public async Task Time_series_tables_are_hypertables_with_rollups_and_retention()
    {
        var hypertables = await ScalarAsync<string>(
            "SELECT string_agg(hypertable_name, ',' ORDER BY hypertable_name) FROM timescaledb_information.hypertables WHERE hypertable_schema = 'public'");
        Assert.Equal("filesystem_metrics,host_metrics,network_metrics,temperature_metrics", hypertables);

        var rollups = await ScalarAsync<string>(
            "SELECT string_agg(view_name, ',' ORDER BY view_name) FROM timescaledb_information.continuous_aggregates");
        Assert.Equal("filesystem_metrics_1h,host_metrics_1h,host_metrics_5m,network_metrics_1h,temperature_metrics_1h", rollups);

        var rawRetention = await ScalarAsync<string>(
            "SELECT config->>'drop_after' FROM timescaledb_information.jobs WHERE proc_name = 'policy_retention' AND hypertable_name = 'host_metrics'");
        Assert.Equal("14 days", rawRetention);
    }
}
