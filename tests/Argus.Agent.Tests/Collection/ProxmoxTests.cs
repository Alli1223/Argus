using System.Net;
using System.Text;
using System.Text.Json;
using Argus.Agent.Collection.Proxmox;
using Argus.Agent.Commands;
using Argus.Agent.Configuration;
using Argus.Contracts.Agent;
using Microsoft.Extensions.Logging.Abstractions;

namespace Argus.Agent.Tests.Collection;

public sealed class ProxmoxParsersTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    [Fact]
    public void The_local_node_is_the_one_marked_local()
    {
        var status = Json("""
            [{"type":"cluster","name":"home","nodes":2},
             {"type":"node","name":"pve1","local":0,"online":1},
             {"type":"node","name":"pve2","local":1,"online":1}]
            """);

        Assert.Equal("pve2", ProxmoxParsers.LocalNode(status));
        Assert.Null(ProxmoxParsers.LocalNode(Json("""[{"type":"cluster","name":"home"}]""")));
    }

    [Fact]
    public void Virtual_machines_are_read_with_what_they_use()
    {
        var list = Json("""
            [{"vmid":100,"name":"web","status":"running","qmpstatus":"running","cpus":4,"cpu":0.0525,"mem":2147483648,
              "maxmem":4294967296,"maxdisk":34359738368,"uptime":7200,"tags":"prod;web"},
             {"vmid":101,"name":"paused","status":"running","qmpstatus":"paused","cpus":2,"cpu":0,"uptime":60},
             {"vmid":9000,"name":"debian-template","status":"stopped","template":1,"cpus":2,"cpu":0,"mem":0,"maxmem":2147483648},
             {"name":"broken"}]
            """);

        var guests = ProxmoxParsers.ParseGuests(list, ProxmoxParsers.VirtualMachine);

        Assert.Equal([100, 101, 9000], guests.Select(guest => guest.Id));
        var web = guests[0];
        Assert.Equal(("web", "running", 5.25, 4.0, 7200L), (web.Name, web.Status, web.CpuPercent, web.Cpus, web.UptimeSeconds));
        Assert.Equal(2147483648, web.MemoryBytes);
        Assert.Equal(["prod", "web"], web.Tags);
        Assert.Equal("paused", guests[1].Status);
        Assert.True(guests[2].Template);
        Assert.Null(guests[2].CpuPercent);
        Assert.Null(guests[2].MemoryBytes);
    }

    [Fact]
    public void Containers_whose_ids_are_strings_are_read_too()
    {
        var guests = ProxmoxParsers.ParseGuests(
            Json("""[{"vmid":"105","status":"stopped","type":"lxc","lock":"backup","maxmem":536870912}]"""), ProxmoxParsers.Container);

        var guest = Assert.Single(guests);
        Assert.Equal((105, "lxc", "CT 105", "stopped", "backup"), (guest.Id, guest.Type, guest.Name, guest.Status, guest.Lock));
    }

    [Theory]
    [InlineData("""{"status":"running"}""", null)]
    [InlineData("""{"status":"stopped","exitstatus":"OK"}""", "OK")]
    [InlineData("""{"status":"stopped","exitstatus":"VM 100 already running"}""", "VM 100 already running")]
    public void Tasks_finish_with_their_exit_status(string status, string? expected) =>
        Assert.Equal(expected, ProxmoxParsers.TaskOutcome(Json(status)));
}

public sealed class ProxmoxCommandsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A Proxmox VE API that answers each request from <paramref name="answer"/>, and keeps what it was asked.</summary>
    private sealed class FakeApi(Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> answer) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method} {request.RequestUri!.PathAndQuery} {request.Headers.Authorization}");
            var (status, body) = answer(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static (ProxmoxCommands Commands, FakeApi Api) Create(Func<HttpRequestMessage, (HttpStatusCode, string)> answer, bool actions = true)
    {
        var api = new FakeApi(answer);
        var node = new ProxmoxNode(new ProxmoxClient(api, "https://127.0.0.1:8006", "argus@pve!agent=secret"));
        var config = new AgentConfig { ProxmoxToken = "argus@pve!agent=secret", ProxmoxActions = actions };
        return (new ProxmoxCommands(config, node, TimeProvider.System, NullLogger<ProxmoxCommands>.Instance), api);
    }

    private static (HttpStatusCode, string) Standard(HttpRequestMessage request, string taskStatus) => request.RequestUri!.AbsolutePath switch
    {
        "/api2/json/cluster/status" => (HttpStatusCode.OK, """{"data":[{"type":"node","name":"pve","local":1}]}"""),
        var path when path.EndsWith("/status", StringComparison.Ordinal) && path.Contains("/tasks/", StringComparison.Ordinal) =>
            (HttpStatusCode.OK, $$"""{"data":{{taskStatus}}}"""),
        _ when request.Method == HttpMethod.Post => (HttpStatusCode.OK, """{"data":"UPID:pve:00001234:0000ABCD:66000000:qmstart:100:argus@pve!agent:"}"""),
        _ => (HttpStatusCode.NotFound, "{}"),
    };

    private static AgentCommand Command(AgentCommandKind kind, int id = 100, string type = "qemu") =>
        new() { Id = "c1", Kind = kind, GuestId = id, GuestType = type };

    [Fact]
    public async Task Starting_posts_to_the_guest_and_waits_for_the_task()
    {
        var (commands, api) = Create(request => Standard(request, """{"status":"stopped","exitstatus":"OK"}"""));

        var result = await commands.RunAsync(Command(AgentCommandKind.GuestStart), Ct);

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("POST /api2/json/nodes/pve/qemu/100/status/start PVEAPIToken argus@pve!agent=secret", api.Requests);
        Assert.Contains(api.Requests, request => request.StartsWith("GET /api2/json/nodes/pve/tasks/UPID%3Apve%3A", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failed_task_fails_the_command_with_its_reason()
    {
        var (commands, api) = Create(request => Standard(request, """{"status":"stopped","exitstatus":"CT 200 is locked (backup)"}"""));

        var result = await commands.RunAsync(Command(AgentCommandKind.GuestShutdown, 200, "lxc"), Ct);

        Assert.False(result.Succeeded);
        Assert.Equal("CT 200 is locked (backup)", result.Error);
        Assert.Contains(api.Requests, request => request.StartsWith("POST /api2/json/nodes/pve/lxc/200/status/shutdown", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refusals_are_explained()
    {
        var (commands, _) = Create(request => request.Method == HttpMethod.Post ? (HttpStatusCode.Forbidden, "{}") : Standard(request, "{}"));

        var result = await commands.RunAsync(Command(AgentCommandKind.GuestStop), Ct);

        Assert.False(result.Succeeded);
        Assert.Contains("--proxmox-actions", result.Error);
    }

    [Fact]
    public async Task Nothing_happens_without_permission_or_with_odd_guests()
    {
        var (off, offApi) = Create(request => Standard(request, "{}"), actions: false);
        Assert.False((await off.RunAsync(Command(AgentCommandKind.GuestStart), Ct)).Succeeded);
        Assert.Empty(offApi.Requests);

        var (on, onApi) = Create(request => Standard(request, "{}"));
        Assert.False((await on.RunAsync(Command(AgentCommandKind.GuestStart, type: "../../access"), Ct)).Succeeded);
        Assert.False((await on.RunAsync(Command(AgentCommandKind.GuestStart, id: 0), Ct)).Succeeded);
        Assert.Empty(onApi.Requests);
    }

    [Fact]
    public void Guests_are_reported_when_they_change()
    {
        var running = false;
        var api = new FakeApi(request => request.RequestUri!.AbsolutePath switch
        {
            "/api2/json/cluster/status" => (HttpStatusCode.OK, """{"data":[{"type":"node","name":"pve","local":1}]}"""),
            "/api2/json/version" => (HttpStatusCode.OK, """{"data":{"version":"8.2.4","release":"8.2"}}"""),
            "/api2/json/nodes/pve/qemu" => (HttpStatusCode.OK, $$"""{"data":[{"vmid":100,"name":"web","status":"{{(running ? "running" : "stopped")}}"}]}"""),
            "/api2/json/nodes/pve/lxc" => (HttpStatusCode.OK, """{"data":[{"vmid":"101","name":"dns","status":"running"}]}"""),
            _ => (HttpStatusCode.NotFound, "{}"),
        });
        var guests = new ProxmoxGuests(
            new ProxmoxNode(new ProxmoxClient(api, "https://127.0.0.1:8006", "t")), true, TimeProvider.System, NullLogger<ProxmoxGuests>.Instance);

        var first = guests.Collect();
        Assert.NotNull(first);
        Assert.Equal(("pve", "8.2.4", true), (first.Node, first.Version, first.ActionsEnabled));
        Assert.Equal([(100, "qemu"), (101, "lxc")], first.Guests.Select(guest => (guest.Id, guest.Type)));

        Assert.Null(guests.Collect());
        running = true;
        Assert.Equal("running", guests.Collect()!.Guests[0].Status);
    }

    [Fact]
    public void A_refused_token_is_reported_as_the_problem()
    {
        var api = new FakeApi(_ => (HttpStatusCode.Unauthorized, ""));
        var guests = new ProxmoxGuests(
            new ProxmoxNode(new ProxmoxClient(api, "https://127.0.0.1:8006", "t")), false, TimeProvider.System, NullLogger<ProxmoxGuests>.Instance);

        Assert.Contains("refused the agent's API token", guests.Collect()!.Problem);
    }

    [Fact]
    public void Guest_commands_are_offered_only_with_a_token_and_permission()
    {
        Assert.DoesNotContain(AgentCommandKind.GuestStart, CommandWorker.Supported(new AgentConfig { ProxmoxActions = true }));
        Assert.DoesNotContain(AgentCommandKind.GuestStart, CommandWorker.Supported(new AgentConfig { ProxmoxToken = "t" }));
        Assert.Contains(AgentCommandKind.GuestStop, CommandWorker.Supported(new AgentConfig { ProxmoxToken = "t", ProxmoxActions = true }));
    }
}
