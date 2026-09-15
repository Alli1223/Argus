using System.Reflection;
using Argus.Server.Infrastructure;
using Microsoft.Extensions.Options;

namespace Argus.Server.Features.Info;

/// <param name="PublicUrl">Address agents and people use to reach the server, when configured.</param>
public sealed record ServerInfo(string Name, string Version, string? PublicUrl);

public static class InfoEndpoints
{
    private static readonly string Version =
        typeof(InfoEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    public static IEndpointRouteBuilder MapInfoEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/info", (IOptions<ArgusOptions> options) =>
                new ServerInfo("Argus", Version, options.Value.PublicUrl?.TrimEnd('/')))
            .WithName("GetServerInfo")
            .WithTags("Info")
            .AllowAnonymous();

        return routes;
    }
}
