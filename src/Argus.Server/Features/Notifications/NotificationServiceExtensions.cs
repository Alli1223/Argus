using Argus.Server.Features.Alerts;
using Argus.Server.Infrastructure;

namespace Argus.Server.Features.Notifications;

public static class NotificationServiceExtensions
{
    public static IServiceCollection AddArgusNotifications(this IServiceCollection services)
    {
        services.AddValidatedOptions<NotificationOptions>(NotificationOptions.SectionName);
        services.AddValidatedOptions<SmtpOptions>(SmtpOptions.SectionName);
        services.AddValidatedOptions<ReportOptions>(ReportOptions.SectionName);

        services.AddSingleton<NotificationSignal>();
        services.AddScoped<IAlertEventSink, NotificationAlertSink>();
        services.AddScoped<NotificationDispatcher>();
        services.AddSingleton<IEmailTransport, SmtpEmailTransport>();
        services.AddSingleton<INotificationSender, EmailNotificationSender>();

        services.AddHttpClient(WebhookNotificationSender.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"Argus/{ServerVersion.Current}");
        });
        foreach (var kind in WebhookNotificationSender.Kinds)
        {
            services.AddSingleton<INotificationSender>(provider => ActivatorUtilities.CreateInstance<WebhookNotificationSender>(provider, kind));
        }

        services.AddHostedService<NotificationDispatchService>();

        services.AddScoped<ReportBuilder>();
        services.AddScoped<ReportScheduler>();
        services.AddHostedService<ReportSchedulingService>();
        return services;
    }
}
