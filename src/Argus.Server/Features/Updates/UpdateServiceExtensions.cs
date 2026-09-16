using Argus.Server.Infrastructure;

namespace Argus.Server.Features.Updates;

public static class UpdateServiceExtensions
{
    public static IServiceCollection AddArgusUpdates(this IServiceCollection services)
    {
        services.AddValidatedOptions<UpdateOptions>(UpdateOptions.SectionName);

        // Agent builds are around 15 MB, so downloads get more time than API calls usually need.
        services.AddHttpClient(ReleaseSource.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"Argus/{ServerVersion.Current}");
        });

        services.AddSingleton<ReleaseSource>();
        services.AddSingleton<UpdateStatus>();
        services.AddSingleton<UpdateChecker>();
        services.AddSingleton<AgentPackages>();
        services.AddScoped<AgentUpdates>();
        services.AddHostedService<UpdateCheckService>();
        return services;
    }
}
