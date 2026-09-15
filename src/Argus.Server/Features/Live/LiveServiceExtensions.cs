using System.Text.Json.Serialization;
using Argus.Server.Features.Alerts;

namespace Argus.Server.Features.Live;

public static class LiveServiceExtensions
{
    public const string HubPath = "/hubs/live";

    public static IServiceCollection AddArgusLive(this IServiceCollection services)
    {
        services.AddSignalR()
            .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services.AddSingleton<LiveUpdates>();
        services.AddSingleton<IAlertEventSink, LiveAlertEventSink>();
        services.AddSingleton<HostStatusMonitor>();
        services.AddHostedService(provider => provider.GetRequiredService<HostStatusMonitor>());
        return services;
    }

    /// <summary>The hub requires a signed-in user like every other endpoint (the fallback policy).</summary>
    public static IEndpointRouteBuilder MapArgusLive(this IEndpointRouteBuilder routes)
    {
        routes.MapHub<LiveHub>(HubPath);
        return routes;
    }
}
