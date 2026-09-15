using Argus.Server.Features.Alerts;

namespace Argus.Server.Tests.Alerts;

public sealed class ServiceFailureDecisionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-15T12:00:00Z");

    [Fact]
    public void Fires_once_the_failure_has_lasted_the_duration()
    {
        Assert.False(AlertDecision.EvaluateServiceFailure(Now.AddSeconds(-30), TimeSpan.FromMinutes(1), Now).Fire);
        Assert.True(AlertDecision.EvaluateServiceFailure(Now.AddMinutes(-2), TimeSpan.FromMinutes(1), Now).Fire);
    }

    [Fact]
    public void Fires_at_once_without_a_duration_even_if_the_agent_clock_runs_ahead()
    {
        Assert.True(AlertDecision.EvaluateServiceFailure(Now.AddSeconds(30), TimeSpan.Zero, Now).Fire);
    }

    [Fact]
    public void Titles_name_the_service_and_the_host()
    {
        var rule = new AlertRule { Metric = AlertMetric.ServiceFailed };

        Assert.Equal("nginx.service failed on web-1", AlertDecision.Title(rule, "web-1", "nginx.service"));
    }
}
