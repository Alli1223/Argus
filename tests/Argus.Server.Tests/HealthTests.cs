using System.Text.Json;
using Argus.Server.Tests.Infrastructure;

namespace Argus.Server.Tests;

public sealed class HealthTests(ArgusAppFixture app) : IClassFixture<ArgusAppFixture>
{
    [Fact]
    public async Task Live_endpoint_is_healthy()
    {
        var response = await app.CreateClient().GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Ready_endpoint_reports_a_healthy_migrated_database()
    {
        var response = await app.CreateClient().GetAsync("/health/ready", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("Healthy", json.RootElement.GetProperty("checks").GetProperty("database").GetString());
    }
}
