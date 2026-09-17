using Argus.Agent.Configuration;
using Argus.Agent.State;
using Argus.Agent.Transport;

namespace Argus.Agent.Commands;

/// <summary>
/// On machines that allow container actions, keeps a request for commands open with the server and carries
/// out what arrives, so that an action taken in the web app happens within a second or so. Machines that do
/// not allow them never ask.
/// </summary>
internal sealed class CommandWorker(
    AgentConfig config,
    StateStore stateStore,
    ArgusClient client,
    ContainerCommands commands,
    TimeProvider time,
    ILogger<CommandWorker> logger) : BackgroundService
{
    private static readonly TimeSpan WaitForRegistration = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.ContainerActions || !OperatingSystem.IsLinux())
        {
            return;
        }

        logger.LogInformation("Container actions are on: people who can see this machine in Argus can read container logs " +
            "and start, stop and restart its containers");

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
                    var outcome = await commands.RunAsync(command, stoppingToken);
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
}
