using System.Text.Json;
using Argus.Agent.Collection;
using Argus.Agent.Configuration;
using Argus.Agent.State;
using Argus.Agent.Transport;
using Argus.Agent.Updates;
using Argus.Contracts.Agent;

namespace Argus.Agent;

internal static class AgentCommands
{
    private const int InvalidConfiguration = 2;

    public static async Task<int> RunAsync(string? configFile, CancellationToken cancellationToken)
    {
        var builder = AgentHost.CreateBuilder(configFile, out var config);
        if (!Validate(config, configFile))
        {
            return InvalidConfiguration;
        }

        builder.Services.AddHostedService<AgentWorker>();
        using var host = builder.Build();
        await host.RunAsync(cancellationToken);
        return 0;
    }

    public static async Task<int> RegisterAsync(string? configFile, string? server, string? token, CancellationToken cancellationToken)
    {
        var overrides = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(server))
        {
            overrides[nameof(AgentConfig.ServerUrl)] = server;
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            overrides[nameof(AgentConfig.EnrollmentToken)] = token;
        }

        var builder = AgentHost.CreateBuilder(configFile, out var config, overrides);
        if (!Validate(config, configFile))
        {
            return InvalidConfiguration;
        }

        if (string.IsNullOrWhiteSpace(config.EnrollmentToken))
        {
            await Console.Error.WriteLineAsync("An enrollment token is required: pass --token or set EnrollmentToken in the config file.");
            return InvalidConfiguration;
        }

        using var host = builder.Build();
        var result = await host.Services.GetRequiredService<Registrar>().RegisterAsync(config.EnrollmentToken, cancellationToken);
        if (!result.IsSuccess)
        {
            await Console.Error.WriteLineAsync($"Registration failed: {result.Describe()}");
            return 1;
        }

        Console.WriteLine($"Registered as host {result.Value!.HostId}; identity saved to {host.Services.GetRequiredService<StateStore>().FilePath}");
        return 0;
    }

    /// <summary>Collects one sample without contacting the server and prints it, for troubleshooting.</summary>
    public static async Task<int> CollectAsync(CancellationToken cancellationToken)
    {
        using var loggers = LoggerFactory.Create(logging => logging.AddSimpleConsole());
        var collector = new SampleCollector(
            PlatformCollectors.CreateMetricsSource(loggers),
            new ProcessCollector(),
            // Default settings: the config file is often readable only by the service.
            PlatformCollectors.CreateTemperatureSource(loggers, new AgentConfig()),
            PlatformCollectors.CreateServiceStatusSource(loggers),
            TimeProvider.System);
        collector.Prime();

        // Rates (CPU, disk, network) need an interval to measure over.
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        var sample = collector.Collect();
        var inventory = new InventoryReport { AgentVersion = AgentInfo.Version, SystemInfo = PlatformCollectors.CreateSystemInfoSource().Collect() };

        var options = new JsonSerializerOptions(AgentJsonContext.Default.Options) { WriteIndented = true };
        Console.WriteLine(JsonSerializer.Serialize(inventory, options.GetTypeInfo(typeof(InventoryReport))));
        Console.WriteLine(JsonSerializer.Serialize(sample, options.GetTypeInfo(typeof(MetricSample))));
        return 0;
    }

    /// <summary>
    /// Installs the update the server offers this agent: run as root by the systemd updater on Linux, and
    /// from a copy of the agent by the service itself on Windows.
    /// </summary>
    public static async Task<int> ApplyUpdateAsync(string? configFile, string? target, CancellationToken cancellationToken)
    {
        using var loggers = LoggerFactory.Create(logging => logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
        }));
        var logger = loggers.CreateLogger("Argus.Agent.Update");

        // Take the request first, so a failure below cannot make systemd run this again and again.
        if (OperatingSystem.IsLinux() && File.Exists(AgentPaths.LinuxUpdateRequestFile))
        {
            File.Delete(AgentPaths.LinuxUpdateRequestFile);
        }

        AgentHost.CreateBuilder(configFile, out var config);
        if (!Validate(config, configFile))
        {
            return InvalidConfiguration;
        }

        // The server address comes from the config file, which only an administrator can change.
        var state = new StateStore(config.ResolvedStateDirectory).Load();
        if (state is null || !string.Equals(state.ServerUrl.TrimEnd('/'), config.ServerUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("The agent is not registered with {Server}, so there is no update to fetch", config.ServerUrl);
            return 1;
        }

        IServiceControl? service = OperatingSystem.IsWindows()
            ? new WindowsServiceControl(AgentHost.WindowsServiceName)
            : OperatingSystem.IsLinux() ? new SystemdServiceControl(SystemdServiceControl.AgentUnit) : null;
        if (service is null || (target ?? Environment.ProcessPath) is not { } program)
        {
            logger.LogError("This platform has no service manager the updater knows");
            return 1;
        }

        var applier = new UpdateApplier(ArgusClient.Create(config), service, logger, UpdateApplier.DefaultSettleTime);
        return await applier.ApplyAsync(state.AgentKey, program, cancellationToken);
    }

    private static bool Validate(AgentConfig config, string? configFile)
    {
        var errors = config.Validate();
        if (errors.Count == 0)
        {
            return true;
        }

        Console.Error.WriteLine($"Invalid agent configuration (config file: {configFile ?? AgentPaths.DefaultConfigFile}):");
        foreach (var error in errors)
        {
            Console.Error.WriteLine("  - " + error);
        }

        return false;
    }
}
