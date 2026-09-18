namespace Argus.Server.Features.Settings;

public static class SettingsServiceExtensions
{
    /// <summary>Server-wide settings administrators change in the web app, such as the mail server.</summary>
    public static IServiceCollection AddArgusSettings(this IServiceCollection services)
    {
        services.AddSingleton<EmailSettingsStore>();
        services.AddSingleton<IEmailSettings>(provider => provider.GetRequiredService<EmailSettingsStore>());
        return services;
    }
}
