using Argus.Agent.Collection;
using Argus.Agent.Configuration;
using Argus.Agent.State;
using Argus.Agent.Transport;
using Argus.Agent.Updates;

namespace Argus.Agent;

internal static class AgentHost
{
    public const string WindowsServiceName = "ArgusAgent";

    /// <summary>
    /// Builds the agent's host. Configuration comes from the config file, then <c>ARGUS_</c> environment
    /// variables, then <paramref name="overrides"/> (command-line options), later sources winning.
    /// </summary>
    public static HostApplicationBuilder CreateBuilder(
        string? configFile, out AgentConfig config, IDictionary<string, string?>? overrides = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Configuration
            .AddJsonFile(configFile ?? AgentPaths.DefaultConfigFile, optional: true, reloadOnChange: false)
            .AddEnvironmentVariables("ARGUS_");
        if (overrides is not null)
        {
            builder.Configuration.AddInMemoryCollection(overrides);
        }

        builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
        });

        // Both are no-ops unless the agent runs under systemd or the Windows service manager.
        builder.Services.AddSystemd();
        builder.Services.AddWindowsService(options => options.ServiceName = WindowsServiceName);

        config = builder.Configuration.Get<AgentConfig>() ?? new AgentConfig();
        var agentConfig = config;

        builder.Services.AddSingleton(agentConfig);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(new StateStore(agentConfig.ResolvedStateDirectory));
        builder.Services.AddSingleton(services => PlatformCollectors.CreateMetricsSource(services.GetRequiredService<ILoggerFactory>()));
        builder.Services.AddSingleton(_ => PlatformCollectors.CreateSystemInfoSource());
        builder.Services.AddSingleton(services =>
            PlatformCollectors.CreateServiceStatusSource(services.GetRequiredService<ILoggerFactory>()));
        builder.Services.AddSingleton<ProcessCollector>();
        builder.Services.AddSingleton<SampleCollector>();
        builder.Services.AddSingleton(new SampleBuffer(agentConfig.BufferCapacity));
        builder.Services.AddSingleton(_ => ArgusClient.Create(agentConfig));
        builder.Services.AddSingleton<Registrar>();
        builder.Services.AddSingleton(_ => UpdateLaunchers.Create(agentConfig));

        return builder;
    }
}
