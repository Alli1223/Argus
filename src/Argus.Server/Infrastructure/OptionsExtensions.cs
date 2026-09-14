using Microsoft.Extensions.Options;

namespace Argus.Server.Infrastructure;

public static class OptionsExtensions
{
    public static IServiceCollection AddArgusOptions(this IServiceCollection services)
    {
        services.AddValidatedOptions<ArgusOptions>(ArgusOptions.SectionName);
        return services;
    }

    /// <summary>Binds a configuration section (resolved lazily) and validates its data annotations at startup.</summary>
    public static OptionsBuilder<TOptions> AddValidatedOptions<TOptions>(this IServiceCollection services, string sectionName)
        where TOptions : class =>
        services.AddOptions<TOptions>()
            .BindConfiguration(sectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
}
