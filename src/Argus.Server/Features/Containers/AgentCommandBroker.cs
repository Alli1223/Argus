using System.Collections.Concurrent;
using System.Threading.Channels;
using Argus.Contracts.Agent;

namespace Argus.Server.Features.Containers;

/// <summary>
/// Hands commands to agents and their answers back to whoever asked. Agents that allow container actions
/// keep a request open (<see cref="WaitForCommandsAsync"/>); a command given to one waits for its answer
/// for a while, and one that gives up waiting is never handed out, so a stop nobody is waiting for any
/// more does not happen later. Commands live only in memory: Argus runs as one server.
/// </summary>
public sealed class AgentCommandBroker(TimeProvider time)
{
    /// <summary>How long a request for commands is held open.</summary>
    public static readonly TimeSpan CommandWait = TimeSpan.FromSeconds(AgentLimits.CommandWaitSeconds);

    /// <summary>How long after its last request an agent still counts as listening.</summary>
    private static readonly TimeSpan ListeningGrace = CommandWait + TimeSpan.FromSeconds(15);

    private sealed class HostQueue
    {
        public Channel<AgentCommand> Pending { get; } = Channel.CreateUnbounded<AgentCommand>();

        public long LastRequestTicks;
    }

    private readonly ConcurrentDictionary<Guid, HostQueue> _hosts = new();
    private readonly ConcurrentDictionary<string, (Guid HostId, TaskCompletionSource<AgentCommandResult> Answer)> _waiting = new();

    /// <summary>Whether the host's agent is asking for commands, which it only does when it allows container actions.</summary>
    public bool IsListening(Guid hostId) =>
        _hosts.TryGetValue(hostId, out var queue)
        && time.GetUtcNow() - new DateTimeOffset(Interlocked.Read(ref queue.LastRequestTicks), TimeSpan.Zero) < ListeningGrace;

    /// <summary>Gives a command to the host's agent and waits for its answer; null when none came in time.</summary>
    public async Task<AgentCommandResult?> SendAsync(Guid hostId, AgentCommand command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var answer = new TaskCompletionSource<AgentCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiting[command.Id] = (hostId, answer);
        try
        {
            Queue(hostId).Pending.Writer.TryWrite(command);
            return await answer.Task.WaitAsync(timeout, time, cancellationToken);
        }
        catch (TimeoutException)
        {
            return null;
        }
        finally
        {
            _waiting.TryRemove(command.Id, out _);
        }
    }

    /// <summary>The commands waiting for the host's agent, once there are any or <see cref="CommandWait"/> has passed.</summary>
    public async Task<List<AgentCommand>> WaitForCommandsAsync(Guid hostId, CancellationToken cancellationToken)
    {
        var queue = Queue(hostId);
        Touch(queue);
        var commands = new List<AgentCommand>();
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(CommandWait);
        try
        {
            while (commands.Count == 0)
            {
                Take(await queue.Pending.Reader.ReadAsync(wait.Token), commands);
            }

            while (queue.Pending.Reader.TryRead(out var more))
            {
                Take(more, commands);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        Touch(queue);
        return commands;
    }

    /// <summary>Takes an agent's answer; answers to commands this host was not given, or nobody waits for, are ignored.</summary>
    public bool Complete(Guid hostId, AgentCommandResult result) =>
        _waiting.TryGetValue(result.Id, out var waiting) && waiting.HostId == hostId && waiting.Answer.TrySetResult(result);

    // Commands whose sender stopped waiting are dropped rather than carried out late.
    private void Take(AgentCommand command, List<AgentCommand> commands)
    {
        if (_waiting.ContainsKey(command.Id))
        {
            commands.Add(command);
        }
    }

    private HostQueue Queue(Guid hostId) => _hosts.GetOrAdd(hostId, _ => new HostQueue());

    private void Touch(HostQueue queue) => Interlocked.Exchange(ref queue.LastRequestTicks, time.GetUtcNow().UtcTicks);
}
