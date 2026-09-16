using Argus.Server.Infrastructure;
using Microsoft.Extensions.Options;

namespace Argus.Server.Features.Info;

/// <param name="PublicUrl">Address agents and people use to reach the server, when configured.</param>
public sealed record ServerInfo(string Name, string Version, string? PublicUrl);

public static class InfoEndpoints
{
    public static IEndpointRouteBuilder MapInfoEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/info", (IOptions<ArgusOptions> options) =>
                new ServerInfo("Argus", ServerVersion.Current, options.Value.PublicUrl?.TrimEnd('/')))
            .WithName("GetServerInfo")
            .WithTags("Info")
            .AllowAnonymous();

        return routes;
    }
}
