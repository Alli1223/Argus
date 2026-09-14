namespace Argus.Server.Infrastructure;

public static class OptionsExtensions
{
    public static IServiceCollection AddArgusOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<ArgusOptions>(configuration, ArgusOptions.SectionName);
        return services;
    }

    /// <summary>Binds a configuration section and validates its data annotations at startup.</summary>
    public static IServiceCollection AddValidatedOptions<TOptions>(
        this IServiceCollection services, IConfiguration configuration, string sectionName)
        where TOptions : class
    {
        services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        return services;
    }
}
