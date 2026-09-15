using Argus.Server.Infrastructure;

namespace Argus.Server.Features.Alerts;

public static class AlertServiceExtensions
{
    public static IServiceCollection AddArgusAlerts(this IServiceCollection services)
    {
        services.AddValidatedOptions<AlertOptions>(AlertOptions.SectionName);
        services.AddScoped<AlertEvaluator>();
        services.AddSingleton<IAlertEventSink, LoggingAlertEventSink>();
        services.AddHostedService<AlertEvaluationService>();
        return services;
    }
}
