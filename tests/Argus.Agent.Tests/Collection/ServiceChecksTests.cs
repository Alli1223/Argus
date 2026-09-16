using Argus.Agent.Collection;
using Argus.Agent.Collection.Linux;
using Argus.Contracts.Agent;
using Microsoft.Extensions.Time.Testing;

namespace Argus.Agent.Tests.Collection;

public sealed class SystemdServicesTests
{
    [Fact]
    public void Reads_failed_units_from_systemctl()
    {
        const string output = """
            nginx.service  loaded failed failed A high performance web server and a reverse proxy server
            ● backup.service loaded failed failed Nightly backup
            quiet.service loaded failed failed
            """;

        var problems = SystemdServices.ParseFailedUnits(output);

        Assert.Equal(["nginx.service", "backup.service", "quiet.service"], problems.Select(problem => problem.Name));
        Assert.Equal("A high performance web server and a reverse proxy server", problems[0].Description);
        Assert.Equal("Nightly backup", problems[1].Description);
        Assert.Null(problems[2].Description);
        Assert.All(problems, problem => Assert.Equal("failed", problem.State));
    }

    [Fact]
    public void Ignores_blank_and_malformed_lines()
    {
        Assert.Empty(SystemdServices.ParseFailedUnits("\n   \nnot enough columns\n"));
    }
}

public sealed class SampleCollectorServiceTests
{
    private sealed class FixedMetrics : ISystemMetricsSource
    {
        public void Prime()
        {
        }

        public SystemReading Collect() => new(
            new CpuMetrics { UsagePercent = 1 },
            new MemoryMetrics { TotalBytes = 100, UsedBytes = 10, AvailableBytes = 90 },
            Load: null,
            DiskIo: null,
            Network: null,
            UptimeSeconds: 60,
            Filesystems: [],
            Interfaces: []);
    }

    private sealed class CountingServices : IServiceStatusSource
    {
        public int Checks { get; private set; }

        public IReadOnlyList<ServiceProblem>? Collect()
        {
            Checks++;
            return [new ServiceProblem { Name = "cron.service", State = "failed" }];
        }
    }

    [Fact]
    public void Checks_services_at_most_once_a_minute()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-09-15T12:00:00Z"));
        var services = new CountingServices();
        var collector = new SampleCollector(new FixedMetrics(), new ProcessCollector(), new NoTemperatures(), services, time) { TopProcessCount = 0 };

        var first = collector.Collect();
        time.Advance(TimeSpan.FromSeconds(15));
        var second = collector.Collect();
        time.Advance(TimeSpan.FromSeconds(45));
        var third = collector.Collect();

        Assert.NotNull(first.FailedServices);
        Assert.Null(second.FailedServices);
        Assert.NotNull(third.FailedServices);
        Assert.Equal(2, services.Checks);
    }
}
