using Argus.Server.Infrastructure;

namespace Argus.Server.Features.Metrics;

public static class MetricsServiceExtensions
{
    public static IServiceCollection AddArgusMetrics(this IServiceCollection services)
    {
        services.AddValidatedOptions<IngestOptions>(IngestOptions.SectionName);
        services.AddValidatedOptions<RetentionOptions>(RetentionOptions.SectionName);
        services.AddSingleton<MetricsIngestor>();
        services.AddSingleton<TimeSeriesQueries>();
        services.AddSingleton<Containers.ContainerStore>();
        return services;
    }
}
