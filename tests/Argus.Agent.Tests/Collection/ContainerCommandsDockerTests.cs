using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Argus.Agent.Collection.Containers;
using Argus.Agent.Commands;
using Argus.Agent.Configuration;
using Argus.Contracts.Agent;
using Microsoft.Extensions.Logging.Abstractions;

namespace Argus.Agent.Tests.Collection;

/// <summary>Container commands against the Docker on the machine running the tests, when there is one.</summary>
public sealed class ContainerCommandsDockerTests
{
    private const string Socket = "/var/run/docker.sock";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A plain client for what the agent itself never does, such as creating containers.</summary>
    private static HttpClient Docker() => new(new SocketsHttpHandler
    {
        ConnectCallback = async (_, cancellationToken) =>
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(Socket), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        },
    })
    { BaseAddress = new Uri("http://docker/"), Timeout = TimeSpan.FromMinutes(2) };

    private static async Task<bool> DockerAvailableAsync(HttpClient docker)
    {
        if (!OperatingSystem.IsLinux() || !File.Exists(Socket))
        {
            return false;
        }

        try
        {
            return (await docker.GetAsync("_ping", Ct)).IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private static async Task<bool> RunningAsync(HttpClient docker, string name)
    {
        using var inspected = JsonDocument.Parse(await docker.GetStringAsync($"containers/{name}/json", Ct));
        return inspected.RootElement.GetProperty("State").GetProperty("Running").GetBoolean();
    }

    [Fact]
    public async Task Start_logs_restart_and_stop_work_on_a_real_container()
    {
        using var docker = Docker();
        Assert.SkipUnless(await DockerAvailableAsync(docker), "Docker is not available here");

        var name = $"argus-test-{Guid.NewGuid():N}"[..24];
        (await docker.PostAsync("images/create?fromImage=alpine&tag=3", null, Ct)).EnsureSuccessStatusCode();
        var create = await docker.PostAsJsonAsync($"containers/create?name={name}", new
        {
            Image = "alpine:3",
            Cmd = new[] { "sh", "-c", "echo hello from argus; echo oops >&2; exec sleep 300" },
        }, Ct);
        create.EnsureSuccessStatusCode();

        using var commands = new ContainerCommands(new AgentConfig { ContainerActions = true }, NullLogger<ContainerCommands>.Instance);
        AgentCommand Command(AgentCommandKind kind) => new() { Id = kind.ToString(), Kind = kind, Container = name, Tail = 10 };
        try
        {
            Assert.True((await commands.RunAsync(Command(AgentCommandKind.ContainerStart), Ct)).Succeeded);
            Assert.True(await RunningAsync(docker, name));
            // Starting what already runs is fine too.
            Assert.True((await commands.RunAsync(Command(AgentCommandKind.ContainerStart), Ct)).Succeeded);

            AgentCommandResult logs = new() { Id = "", Succeeded = false };
            for (var attempt = 0; attempt < 20 && logs.Logs is not { Count: 2 }; attempt++)
            {
                logs = await commands.RunAsync(Command(AgentCommandKind.ContainerLogs), Ct);
                await Task.Delay(100, Ct);
            }

            Assert.True(logs.Succeeded, logs.Error);
            Assert.Contains(logs.Logs!, line => line is { Stream: "stdout", Text: "hello from argus" } && line.Time is not null);
            Assert.Contains(logs.Logs!, line => line is { Stream: "stderr", Text: "oops" });

            Assert.True((await commands.RunAsync(Command(AgentCommandKind.ContainerRestart), Ct)).Succeeded);
            Assert.True(await RunningAsync(docker, name));

            Assert.True((await commands.RunAsync(Command(AgentCommandKind.ContainerStop), Ct)).Succeeded);
            Assert.False(await RunningAsync(docker, name));

            var missing = await commands.RunAsync(Command(AgentCommandKind.ContainerStart) with { Container = name + "-gone" }, Ct);
            Assert.False(missing.Succeeded);
            Assert.Contains("No such container", missing.Error);
        }
        finally
        {
            await docker.DeleteAsync($"containers/{name}?force=true", CancellationToken.None);
        }
    }
}
