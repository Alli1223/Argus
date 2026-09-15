using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Argus.Server.Features.Health;

public static class HealthEndpoints
{
    /// <summary>Tag for checks that must pass before the server should receive traffic.</summary>
    public const string ReadyTag = "ready";

    public static IHealthChecksBuilder AddArgusHealthChecks(this IServiceCollection services) =>
        services.AddHealthChecks();

    public static IEndpointRouteBuilder MapArgusHealthChecks(this IEndpointRouteBuilder routes)
    {
        // Liveness: the process is up and serving requests; dependencies are not checked.
        routes.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous();

        // Readiness: dependencies such as the database are reachable.
        routes.MapHealthChecks("/health/ready", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains(ReadyTag),
                ResponseWriter = WriteJsonAsync,
            })
            .AllowAnonymous();

        return routes;
    }

    private static Task WriteJsonAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return JsonSerializer.SerializeAsync(context.Response.Body, new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(entry => entry.Key, entry => entry.Value.Status.ToString()),
        });
    }
}
