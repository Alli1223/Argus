using System.Net;
using Argus.Contracts.Agent;
using Argus.Server.Features.Metrics;
using Argus.Server.Tests.Infrastructure;

namespace Argus.Server.Tests.Metrics;

public sealed class ServiceChecksTests(ArgusAppFixture app) : IClassFixture<ArgusAppFixture>
{
    private static readonly TimeSpan Precision = TimeSpan.FromMilliseconds(1);

    private static ServiceProblem Failed(string name) => new() { Name = name, Description = $"{name} unit", State = "failed" };

    private static MetricSample Checked(DateTimeOffset time, params ServiceProblem[] failures) =>
        MetricsIngestionTests.FullSample(time) with { FailedServices = failures };

    [Fact]
    public async Task Failures_keep_the_time_they_were_first_seen_until_they_recover()
    {
        var owner = await app.CreateOwnerAsync("services-a@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "services-a-1");
        var start = DateTimeOffset.UtcNow.AddMinutes(-10);

        await agent.SendSamplesAsync(Checked(start, Failed("nginx.service")));
        await agent.SendSamplesAsync(Checked(start.AddMinutes(1), Failed("nginx.service"), Failed("cron.service")));

        var status = await owner.GetJsonAsync<ServiceStatus>($"/api/hosts/{hostId}/services");
        Assert.Equal(start.AddMinutes(1), status!.CheckedAt!.Value, Precision);
        Assert.Equal(start, status.Failures.Single(failure => failure.Service == "nginx.service").Since, Precision);
        Assert.Equal(start.AddMinutes(1), status.Failures.Single(failure => failure.Service == "cron.service").Since, Precision);
        Assert.Equal("nginx.service unit", status.Failures.Single(failure => failure.Service == "nginx.service").Description);

        await agent.SendSamplesAsync(Checked(start.AddMinutes(2), Failed("cron.service")));

        var later = await owner.GetJsonAsync<ServiceStatus>($"/api/hosts/{hostId}/services");
        Assert.Equal(["cron.service"], later!.Failures.Select(failure => failure.Service));
    }

    [Fact]
    public async Task A_late_older_check_or_a_sample_without_one_changes_nothing()
    {
        var owner = await app.CreateOwnerAsync("services-b@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "services-b-1");
        var start = DateTimeOffset.UtcNow.AddMinutes(-10);

        await agent.SendSamplesAsync(Checked(start.AddMinutes(2), Failed("backup.service")));
        await agent.SendSamplesAsync(Checked(start.AddMinutes(1)));
        await agent.SendSamplesAsync(MetricsIngestionTests.FullSample(start.AddMinutes(3)));

        var status = await owner.GetJsonAsync<ServiceStatus>($"/api/hosts/{hostId}/services");
        Assert.Equal(start.AddMinutes(2), status!.CheckedAt!.Value, Precision);
        Assert.Equal(["backup.service"], status.Failures.Select(failure => failure.Service));
    }

    [Fact]
    public async Task Hosts_that_never_checked_have_no_check_time()
    {
        var owner = await app.CreateOwnerAsync("services-c@example.com");
        var (hostId, _) = await app.RegisterHostAsync(owner, "services-c-1");

        var status = await owner.GetJsonAsync<ServiceStatus>($"/api/hosts/{hostId}/services");

        Assert.Null(status!.CheckedAt);
        Assert.Empty(status.Failures);
    }

    [Fact]
    public async Task Only_the_owner_sees_a_hosts_services()
    {
        var owner = await app.CreateOwnerAsync("services-d@example.com");
        var (hostId, _) = await app.RegisterHostAsync(owner, "services-d-1");
        var stranger = await app.CreateOwnerAsync("services-e@example.com");

        var response = await stranger.GetAsync($"/api/hosts/{hostId}/services", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
