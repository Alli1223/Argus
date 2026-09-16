using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Argus.Server.Tests.Infrastructure;

/// <summary>Runs the real server in memory against a test database.</summary>
public sealed class ArgusFactory(
    string connectionString,
    IReadOnlyDictionary<string, string?> settings,
    Action<IServiceCollection>? configureServices = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Argus", connectionString);

        // Tests call far more often than people and agents do; the throttling tests lower these again.
        builder.UseSetting("Argus:RateLimits:AuthPermitsPerMinute", "100000");
        builder.UseSetting("Argus:RateLimits:AgentRegisterPermitsPerMinute", "100000");
        builder.UseSetting("Argus:RateLimits:AgentIngestPermitsPerMinute", "100000");

        // Tests evaluate alert rules and send notifications on demand, at the moments they choose.
        builder.UseSetting("Argus:Alerts:BackgroundEvaluation", "false");
        builder.UseSetting("Argus:Notifications:BackgroundDelivery", "false");

        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        if (configureServices is not null)
        {
            builder.ConfigureTestServices(configureServices);
        }
    }
}
