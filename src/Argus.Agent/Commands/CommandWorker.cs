using Argus.Agent.Configuration;
using Argus.Agent.State;
using Argus.Agent.Transport;
using Argus.Contracts.Agent;

namespace Argus.Agent.Commands;

/// <summary>
/// Keeps a request for commands open with the server and carries out what arrives, so that an action taken
/// in the web app happens within a second or so. Every agent can send Wake-on-LAN packets for the server;
/// container, power and Proxmox VE actions only happen on machines whose configuration allows them.
/// </summary>
internal sealed class CommandWorker(
    AgentConfig config,
    StateStore stateStore,
    ArgusClient client,
    ContainerCommands containers,
    PowerCommands power,
    WakeOnLan wake,
    ProxmoxCommands proxmox,
    TimeProvider time,
    ILogger<CommandWorker> logger) : BackgroundService
{
    private static readonly TimeSpan WaitForRegistration = TimeSpan.FromSeconds(10);

    /// <summary>The commands this agent carries out, which it tells the server in its inventory.</summary>
    public static List<AgentCommandKind> Supported(AgentConfig config)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
        {
            return [];
        }

        List<AgentCommandKind> kinds = [AgentCommandKind.WakeOnLan];
        if (config.ContainerActions && OperatingSystem.IsLinux())
        {
            kinds.AddRange([AgentCommandKind.ContainerStart, AgentCommandKind.ContainerStop, AgentCommandKind.ContainerRestart, AgentCommandKind.ContainerLogs]);
        }

        if (config.PowerActions)
        {
            kinds.AddRange([AgentCommandKind.Shutdown, AgentCommandKind.Restart]);
        }

        if (config.ProxmoxActions && config.ReadsProxmox && OperatingSystem.IsLinux())
        {
            kinds.AddRange([AgentCommandKind.GuestStart, AgentCommandKind.GuestShutdown, AgentCommandKind.GuestReboot, AgentCommandKind.GuestStop]);
        }

        return kinds;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
        {
            return;
        }

        if (config.ContainerActions && OperatingSystem.IsLinux())
        {
            logger.LogInformation("Container actions are on: people who can see this machine in Argus can read container logs " +
                "and start, stop and restart its containers");
        }

        if (config.PowerActions)
        {
            logger.LogInformation("Power actions are on: people who can see this machine in Argus, and their alert rules, " +
                "can shut it down and restart it");
        }

        if (config.ProxmoxActions && config.ReadsProxmox && OperatingSystem.IsLinux())
        {
            logger.LogInformation("Proxmox VE actions are on: people who can see this machine in Argus can start, shut down, " +
                "restart and stop its virtual machines and containers");
        }

        var backoff = new Backoff(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1));
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // The main worker registers the agent; until it has, there is no key to ask with.
                if (stateStore.Load() is not { } state)
                {
                    await Task.Delay(WaitForRegistration, time, stoppingToken);
                    continue;
                }

                var result = await client.GetCommandsAsync(state.AgentKey, stoppingToken);
                if (!result.IsSuccess)
                {
                    var delay = result.RetryAfter ?? backoff.NextDelay();
                    logger.LogDebug("Could not ask for commands ({Reason}); trying again in {Delay:g}", result.Describe(), delay);
                    await Task.Delay(delay, time, stoppingToken);
                    continue;
                }

                backoff.Reset();
                foreach (var command in result.Value!.Commands)
                {
                    var outcome = await RunAsync(command, stoppingToken);
                    var sent = await client.SendCommandResultAsync(state.AgentKey, outcome, stoppingToken);
                    if (!sent.IsSuccess)
                    {
                        logger.LogWarning("Could not report how command {Id} went: {Reason}", command.Id, sent.Describe());
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private Task<AgentCommandResult> RunAsync(AgentCommand command, CancellationToken cancellationToken) => command.Kind switch
    {
        AgentCommandKind.Shutdown or AgentCommandKind.Restart => power.RunAsync(command, cancellationToken),
        AgentCommandKind.WakeOnLan => wake.RunAsync(command, cancellationToken),
        AgentCommandKind.GuestStart or AgentCommandKind.GuestShutdown or AgentCommandKind.GuestReboot or AgentCommandKind.GuestStop =>
            proxmox.RunAsync(command, cancellationToken),
        _ when OperatingSystem.IsLinux() => containers.RunAsync(command, cancellationToken),
        _ => Task.FromResult(new AgentCommandResult { Id = command.Id, Succeeded = false, Error = "Container actions are only on Linux." }),
    };
}
