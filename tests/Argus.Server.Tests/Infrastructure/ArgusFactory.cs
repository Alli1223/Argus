using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Argus.Server.Tests.Infrastructure;

/// <summary>Runs the real server in memory against a test database.</summary>
public sealed class ArgusFactory(string connectionString, IReadOnlyDictionary<string, string?> settings)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Argus", connectionString);

        // Tests sign in and register far more often than people do; the throttling tests lower these again.
        builder.UseSetting("Argus:RateLimits:AuthPermitsPerMinute", "100000");
        builder.UseSetting("Argus:RateLimits:AgentRegisterPermitsPerMinute", "100000");

        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }
    }
}
