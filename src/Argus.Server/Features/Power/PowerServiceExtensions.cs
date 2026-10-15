using Argus.Server.Features.Alerts;

namespace Argus.Server.Features.Power;

public static class PowerServiceExtensions
{
    public static IServiceCollection AddArgusPower(this IServiceCollection services)
    {
        services.AddScoped<PowerService>();
        services.AddSingleton<RulePowerQueue>();
        services.AddScoped<IAlertEventSink, PowerAlertSink>();
        services.AddHostedService<RulePowerActionService>();
        return services;
    }
}
