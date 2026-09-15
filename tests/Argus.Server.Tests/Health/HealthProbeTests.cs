using Argus.Server.Infrastructure;

namespace Argus.Server.Tests.Health;

public sealed class HealthProbeTests
{
    [Theory]
    [InlineData(null, "http://localhost:8080/health/ready")]
    [InlineData("", "http://localhost:8080/health/ready")]
    [InlineData("5000", "http://localhost:5000/health/ready")]
    [InlineData("8081;9090", "http://localhost:8081/health/ready")]
    public void Probes_the_first_http_port(string? ports, string expected) =>
        Assert.Equal(expected, HealthProbe.ProbeUrl(ports));

    [Fact]
    public async Task Reports_failure_when_nothing_answers()
    {
        Assert.Equal(1, await HealthProbe.RunAsync([HealthProbe.Command, "http://127.0.0.1:1/health/ready"]));
    }
}
