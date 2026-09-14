using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Argus.Server.Tests.Infrastructure;

/// <summary>Runs the real server in memory against a test database.</summary>
public sealed class ArgusFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Argus", connectionString);
    }
}
