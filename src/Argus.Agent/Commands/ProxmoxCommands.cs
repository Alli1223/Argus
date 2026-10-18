using Argus.Agent.Collection.Proxmox;
using Argus.Agent.Configuration;
using Argus.Contracts.Agent;

namespace Argus.Agent.Commands;

/// <summary>
/// Starts, shuts down, restarts and stops Proxmox VE guests on this node for the server. Nothing else is
/// possible, and only on machines whose agent configuration sets ProxmoxActions.
/// </summary>
internal sealed class ProxmoxCommands(AgentConfig config, ProxmoxNode? node, TimeProvider time, ILogger<ProxmoxCommands> logger)
{
    /// <summary>
    /// How long to watch the task Proxmox VE starts. Starting and stopping finish within it, or fail; a guest
    /// can take minutes to shut down, so a task still running by then counts as under way.
    /// </summary>
    internal static readonly TimeSpan WatchTask = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    public async Task<AgentCommandResult> RunAsync(AgentCommand command, CancellationToken cancellationToken)
    {
        if (!config.ProxmoxActions || node is null)
        {
            return Failed(command, "Proxmox VE actions are off on this machine.");
        }

        if (command.GuestId is not (> 0 and var id) || command.GuestType is not (ProxmoxParsers.VirtualMachine or ProxmoxParsers.Container))
        {
            return Failed(command, "That is not a Proxmox VE guest.");
        }

        var action = command.Kind switch
        {
            AgentCommandKind.GuestStart => "start",
            AgentCommandKind.GuestShutdown => "shutdown",
            AgentCommandKind.GuestReboot => "reboot",
            AgentCommandKind.GuestStop => "stop",
            _ => null,
        };
        if (action is null)
        {
            return Failed(command, $"This agent does not know how to {command.Kind}.");
        }

        try
        {
            var path = $"nodes/{Uri.EscapeDataString(await node.NameAsync(cancellationToken))}";
            var task = await node.Client.PostAsync($"{path}/{command.GuestType}/{id}/status/{action}", cancellationToken);
            if (await WatchAsync(path, task, cancellationToken) is { } outcome and not "OK")
            {
                logger.LogWarning("Could not {Action} guest {Id}: {Reason}", action, id, outcome);
                return Failed(command, outcome);
            }

            logger.LogInformation("Asked Proxmox VE to {Action} guest {Id}, as asked through Argus", action, id);
            return new AgentCommandResult { Id = command.Id, Succeeded = true };
        }
        catch (ProxmoxException ex)
        {
            logger.LogWarning("Could not {Action} guest {Id}: {Reason}", action, id, ex.Message);
            return Failed(command, ex.Message);
        }
    }

    /// <summary>How the task went, or null when it is still running after <see cref="WatchTask"/>.</summary>
    private async Task<string?> WatchAsync(string path, string task, CancellationToken cancellationToken)
    {
        if (task.Length == 0)
        {
            return null;
        }

        var until = time.GetUtcNow() + WatchTask;
        while (true)
        {
            var status = await node!.Client.GetAsync($"{path}/tasks/{Uri.EscapeDataString(task)}/status", cancellationToken);
            if (ProxmoxParsers.TaskOutcome(status) is { } outcome)
            {
                return outcome;
            }

            if (time.GetUtcNow() >= until)
            {
                return null;
            }

            await Task.Delay(PollInterval, time, cancellationToken);
        }
    }

    private static AgentCommandResult Failed(AgentCommand command, string error) =>
        new() { Id = command.Id, Succeeded = false, Error = error };
}
