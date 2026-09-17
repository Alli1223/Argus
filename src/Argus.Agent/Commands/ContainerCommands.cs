using System.Net;
using System.Text.RegularExpressions;
using Argus.Agent.Collection.Containers;
using Argus.Agent.Configuration;
using Argus.Contracts.Agent;

namespace Argus.Agent.Commands;

/// <summary>
/// Carries out container commands from the server: start, stop and restart a container, or read its logs.
/// Nothing else is possible, and only on machines whose agent configuration sets ContainerActions.
/// </summary>
internal sealed partial class ContainerCommands(AgentConfig config, ILogger<ContainerCommands> logger, DockerClient? client = null)
    : IDisposable
{
    /// <summary>How long Docker gives a container to stop before killing it.</summary>
    private const int StopSeconds = 10;

    private const int DefaultTail = 200;

    private readonly DockerClient _docker = client ?? new DockerClient(config.DockerSocket);
    private readonly bool _enabled = config.ContainerActions;

    public async Task<AgentCommandResult> RunAsync(AgentCommand command, CancellationToken cancellationToken)
    {
        if (!_enabled)
        {
            return Failed(command, "Container actions are off on this machine.");
        }

        if (!ContainerName().IsMatch(command.Container))
        {
            return Failed(command, "That is not a container name.");
        }

        var name = command.Container;
        try
        {
            switch (command.Kind)
            {
                case AgentCommandKind.ContainerStart:
                    await ActAsync($"containers/{name}/start", cancellationToken);
                    break;
                case AgentCommandKind.ContainerStop:
                    await ActAsync($"containers/{name}/stop?t={StopSeconds}", cancellationToken);
                    break;
                case AgentCommandKind.ContainerRestart:
                    await ActAsync($"containers/{name}/restart?t={StopSeconds}", cancellationToken);
                    break;
                case AgentCommandKind.ContainerLogs:
                    return await LogsAsync(command, cancellationToken);
                default:
                    return Failed(command, $"This agent does not know how to {command.Kind}.");
            }

            logger.LogInformation("{Action} container {Container}, as asked through Argus", Describe(command.Kind), name);
            return new AgentCommandResult { Id = command.Id, Succeeded = true };
        }
        catch (DockerException ex)
        {
            logger.LogWarning("Could not {Action} container {Container}: {Reason}", command.Kind, name, ex.Message);
            return Failed(command, ex.Message);
        }
    }

    public void Dispose() => _docker.Dispose();

    /// <summary>Starts, stops or restarts; Docker answers 304 when the container already was as asked.</summary>
    private async Task ActAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _docker.SendAsync(HttpMethod.Post, path, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }
        catch (DockerException ex) when (ex.Status == HttpStatusCode.NotModified)
        {
        }
    }

    private async Task<AgentCommandResult> LogsAsync(AgentCommand command, CancellationToken cancellationToken)
    {
        var tail = Math.Clamp(command.Tail ?? DefaultTail, 1, AgentLimits.MaxLogLines);
        bool tty;
        using (var inspected = await _docker.GetJsonAsync($"containers/{command.Container}/json", cancellationToken))
        {
            tty = inspected.RootElement.TryGetProperty("Config", out var containerConfig)
                && containerConfig.TryGetProperty("Tty", out var flag) && flag.GetBoolean();
        }

        using var response = await _docker.SendAsync(
            HttpMethod.Get,
            $"containers/{command.Container}/logs?stdout=1&stderr=1&timestamps=1&tail={tail}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);

        // Enough lines fit in a couple of megabytes; a container writing huge lines gets fewer of them.
        var buffer = new byte[AgentLimits.MaxLogBytes];
        var read = 0;
        while (read < buffer.Length && await body.ReadAsync(buffer.AsMemory(read), cancellationToken) is var count and > 0)
        {
            read += count;
        }

        var cut = read == buffer.Length && await body.ReadAsync(new byte[1], cancellationToken) > 0;
        var (lines, truncated) = DockerLogs.Parse(buffer.AsSpan(0, read), tty, tail, cut);
        return new AgentCommandResult { Id = command.Id, Succeeded = true, Logs = lines, Truncated = truncated };
    }

    private static AgentCommandResult Failed(AgentCommand command, string error) =>
        new() { Id = command.Id, Succeeded = false, Error = error };

    private static string Describe(AgentCommandKind kind) => kind switch
    {
        AgentCommandKind.ContainerStart => "Started",
        AgentCommandKind.ContainerStop => "Stopped",
        _ => "Restarted",
    };

    /// <summary>Docker's container names, which also keeps anything else out of the request path.</summary>
    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,254}$")]
    private static partial Regex ContainerName();
}
