using System.Reflection;

namespace Argus.Server.Features.Info;

public sealed record ServerInfo(string Name, string Version);

public static class InfoEndpoints
{
    private static readonly ServerInfo Info = new(
        "Argus",
        typeof(InfoEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? "0.0.0");

    public static IEndpointRouteBuilder MapInfoEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/info", () => Info)
            .WithName("GetServerInfo")
            .WithTags("Info")
            .AllowAnonymous();

        return routes;
    }
}
