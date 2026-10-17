using Argus.Server.Features.Notifications;
using Argus.Server.Infrastructure;

namespace Argus.Server.Features.Telegram;

public static class TelegramServiceExtensions
{
    public static IServiceCollection AddArgusTelegram(this IServiceCollection services)
    {
        services.AddValidatedOptions<TelegramOptions>(TelegramOptions.SectionName);

        // Bot API URLs hold the bot's token, so this client writes nothing to the logs.
        services.AddHttpClient(TelegramApi.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(60);
                client.DefaultRequestHeaders.UserAgent.ParseAdd($"Argus/{ServerVersion.Current}");
            })
            .RemoveAllLoggers();

        services.AddSingleton<TelegramApi>();
        services.AddSingleton<TelegramSettingsStore>();
        services.AddSingleton<TelegramBotStatus>();
        services.AddSingleton<INotificationSender, TelegramNotificationSender>();
        services.AddScoped<TelegramUpdateHandler>();
        services.AddHostedService<TelegramPollingService>();
        return services;
    }
}
