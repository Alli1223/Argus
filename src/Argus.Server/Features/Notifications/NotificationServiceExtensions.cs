using Argus.Server.Features.Alerts;
using Argus.Server.Infrastructure;

namespace Argus.Server.Features.Notifications;

public static class NotificationServiceExtensions
{
    public static IServiceCollection AddArgusNotifications(this IServiceCollection services)
    {
        services.AddValidatedOptions<NotificationOptions>(NotificationOptions.SectionName);
        services.AddValidatedOptions<SmtpOptions>(SmtpOptions.SectionName);

        services.AddSingleton<NotificationSignal>();
        services.AddScoped<IAlertEventSink, NotificationAlertSink>();
        services.AddScoped<NotificationDispatcher>();
        services.AddSingleton<IEmailTransport, SmtpEmailTransport>();
        services.AddSingleton<INotificationSender, EmailNotificationSender>();
        services.AddHostedService<NotificationDispatchService>();
        return services;
    }
}
