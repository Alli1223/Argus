using System.Net;
using System.Net.Http.Json;
using Argus.Contracts.Agent;
using Argus.Server.Features.Containers;
using Argus.Server.Tests.Infrastructure;
using Argus.Server.Tests.Metrics;
using Microsoft.AspNetCore.Mvc;

namespace Argus.Server.Tests.Containers;

public class AgentCommandBrokerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AgentCommand Stop(string id = "c1") => new() { Id = id, Kind = AgentCommandKind.ContainerStop, Container = "web" };

    [Fact]
    public async Task Commands_reach_the_listening_agent_and_its_answer_comes_back()
    {
        var broker = new AgentCommandBroker(TimeProvider.System);
        var host = Guid.NewGuid();
        Assert.False(broker.IsListening(host));

        var agent = broker.WaitForCommandsAsync(host, Ct);
        Assert.True(broker.IsListening(host));
        var sent = broker.SendAsync(host, Stop(), TimeSpan.FromSeconds(10), Ct);

        var command = Assert.Single(await agent);
        Assert.False(broker.Complete(Guid.NewGuid(), new AgentCommandResult { Id = command.Id, Succeeded = false }));
        Assert.True(broker.Complete(host, new AgentCommandResult { Id = command.Id, Succeeded = true }));
        Assert.True((await sent)!.Succeeded);
    }

    [Fact]
    public async Task Commands_nobody_waits_for_any_more_are_never_handed_out()
    {
        var broker = new AgentCommandBroker(TimeProvider.System);
        var host = Guid.NewGuid();

        Assert.Null(await broker.SendAsync(host, Stop("stale"), TimeSpan.FromMilliseconds(50), Ct));

        var agent = broker.WaitForCommandsAsync(host, Ct);
        var sent = broker.SendAsync(host, Stop("fresh"), TimeSpan.FromSeconds(10), Ct);
        Assert.Equal("fresh", Assert.Single(await agent).Id);
        broker.Complete(host, new AgentCommandResult { Id = "fresh", Succeeded = true });
        await sent;
    }
}

public sealed class ContainerActionTests(ArgusAppFixture app) : IClassFixture<ArgusAppFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MetricSample Report(bool actions) => MetricsIngestionTests.FullSample(DateTimeOffset.UtcNow) with
    {
        Containers = new ContainerReport
        {
            ActionsEnabled = actions,
            Items = [ContainerChangesTests.Container("shop-web-1")],
        },
    };

    /// <summary>An agent that takes commands for a while, answering each with <paramref name="answer"/>, and keeps what it was asked.</summary>
    private static (Task Loop, List<AgentCommand> Received, CancellationTokenSource Stop) Listen(
        HttpClient agent, Func<AgentCommand, AgentCommandResult> answer)
    {
        var received = new List<AgentCommand>();
        var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var loop = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    using var response = await agent.GetAsync(AgentApi.Commands, stop.Token);
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        continue;
                    }

                    var batch = (await response.Content.ReadFromJsonAsync(AgentJsonContext.Default.AgentCommandBatch, stop.Token))!;
                    foreach (var command in batch.Commands)
                    {
                        lock (received)
                        {
                            received.Add(command);
                        }

                        (await agent.PostAsJsonAsync(AgentApi.CommandResults, answer(command), AgentJsonContext.Default.AgentCommandResult, stop.Token))
                            .EnsureSuccessStatusCode();
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        });
        return (loop, received, stop);
    }

    private async Task<(Guid HostId, HttpClient Owner, HttpClient Agent)> HostAsync(string email, bool actions)
    {
        var owner = await app.CreateOwnerAsync(email);
        var (hostId, agent) = await app.RegisterHostAsync(owner, email + "-machine");
        await agent.SendSamplesAsync(Report(actions));
        return (hostId, owner, agent);
    }

    private static async Task WaitUntilListeningAsync(HttpClient owner, Guid hostId)
    {
        // The agent's first request for commands may not have arrived yet.
        for (var attempt = 0; attempt < 50; attempt++)
        {
            using var probe = await owner.GetAsync($"/api/hosts/{hostId}/containers/shop-web-1/logs?tail=1", Ct);
            if (probe.StatusCode != HttpStatusCode.Conflict)
            {
                return;
            }

            await Task.Delay(100, Ct);
        }
    }

    [Fact]
    public async Task Owners_start_stop_and_restart_containers_and_read_their_logs_through_the_agent()
    {
        var (hostId, owner, agent) = await HostAsync("actions-a@example.com", actions: true);
        var (loop, received, stop) = Listen(agent, command => command.Kind == AgentCommandKind.ContainerLogs
            ? new AgentCommandResult
            {
                Id = command.Id,
                Succeeded = true,
                Logs = [new ContainerLogLine { Stream = "stderr", Text = "listening on :80", Time = DateTimeOffset.UtcNow }],
            }
            : new AgentCommandResult { Id = command.Id, Succeeded = true });
        await WaitUntilListeningAsync(owner, hostId);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/hosts/{hostId}/containers/shop-web-1/stop", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/hosts/{hostId}/containers/shop-web-1/restart", null, Ct)).StatusCode);
        var logs = (await owner.GetJsonAsync<ContainerLogs>($"/api/hosts/{hostId}/containers/shop-web-1/logs?tail=50"))!;
        await stop.CancelAsync();
        await loop;

        Assert.Equal(("stderr", "listening on :80"), (Assert.Single(logs.Lines).Stream, logs.Lines[0].Text));
        lock (received)
        {
            Assert.Contains(received, command => command is { Kind: AgentCommandKind.ContainerStop, Container: "shop-web-1" });
            Assert.Contains(received, command => command is { Kind: AgentCommandKind.ContainerLogs, Tail: 50 });
        }

        var detail = (await owner.GetJsonAsync<ContainerDetail>($"/api/hosts/{hostId}/containers/shop-web-1"))!;
        Assert.Contains(detail.Events, change => change is { Kind: "stop-requested", Detail: "actions-a@example.com" });
        Assert.Contains(detail.Events, change => change.Kind == "restart-requested");
    }

    [Fact]
    public async Task Docker_refusals_are_passed_on()
    {
        var (hostId, owner, agent) = await HostAsync("actions-b@example.com", actions: true);
        var (loop, _, stop) = Listen(agent, command => new AgentCommandResult { Id = command.Id, Succeeded = false, Error = "No such container: shop-web-1" });
        await WaitUntilListeningAsync(owner, hostId);

        var response = await owner.PostAsync($"/api/hosts/{hostId}/containers/shop-web-1/start", null, Ct);
        await stop.CancelAsync();
        await loop;

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("No such container: shop-web-1", (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Detail);
    }

    [Fact]
    public async Task Nothing_goes_to_machines_that_do_not_allow_actions_or_are_not_listening()
    {
        var (offId, offOwner, _) = await HostAsync("actions-c@example.com", actions: false);
        var off = await offOwner.PostAsync($"/api/hosts/{offId}/containers/shop-web-1/stop", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, off.StatusCode);
        Assert.Equal("Container actions are off on this machine", (await off.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Title);

        var (quietId, quietOwner, _) = await HostAsync("actions-d@example.com", actions: true);
        var quiet = await quietOwner.GetAsync($"/api/hosts/{quietId}/containers/shop-web-1/logs", Ct);
        Assert.Equal(HttpStatusCode.Conflict, quiet.StatusCode);
        Assert.Equal("The agent is not listening", (await quiet.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Title);

        var stranger = await app.CreateOwnerAsync("actions-e@example.com");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"/api/hosts/{quietId}/containers/shop-web-1/stop", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await quietOwner.PostAsync($"/api/hosts/{quietId}/containers/no-such/stop", null, Ct)).StatusCode);
    }
}
