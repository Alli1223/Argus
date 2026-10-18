using System.Net;
using System.Net.Http.Json;
using Argus.Contracts.Agent;
using Argus.Server.Features.Proxmox;
using Argus.Server.Tests.Containers;
using Argus.Server.Tests.Infrastructure;
using Argus.Server.Tests.Metrics;

namespace Argus.Server.Tests.Proxmox;

public sealed class ProxmoxTests(ArgusAppFixture app) : IClassFixture<ArgusAppFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ProxmoxGuest Guest(int id, string status, string type = "qemu") => new()
    {
        Id = id,
        Type = type,
        Name = $"guest-{id}",
        Status = status,
        UptimeSeconds = status == "running" ? 3600 : 0,
        CpuPercent = status == "running" ? 12.5 : null,
        MaxMemoryBytes = 4L << 30,
    };

    private static MetricSample Report(DateTimeOffset at, bool actions, params ProxmoxGuest[] guests) =>
        MetricsIngestionTests.FullSample(at) with
        {
            Proxmox = new ProxmoxReport { Node = "pve", Version = "8.2.4", ActionsEnabled = actions, Guests = guests },
        };

    private async Task<(Guid HostId, HttpClient Owner, HttpClient Agent)> HostAsync(string email, bool actions, params ProxmoxGuest[] guests)
    {
        var owner = await app.CreateOwnerAsync(email);
        var (hostId, agent) = await app.RegisterHostAsync(owner, email + "-machine");
        await agent.SendSamplesAsync(Report(DateTimeOffset.UtcNow.AddSeconds(-30), actions, guests));
        return (hostId, owner, agent);
    }

    private static async Task<HostProxmox> GetAsync(HttpClient owner, Guid hostId) =>
        (await owner.GetFromJsonAsync<HostProxmox>($"/api/hosts/{hostId}/proxmox", Ct))!;

    [Fact]
    public async Task Guests_are_listed_and_keep_when_their_status_changed()
    {
        var (hostId, owner, agent) = await HostAsync("pve-list@example.com", false, Guest(100, "running"), Guest(101, "stopped", "lxc"));
        var first = await GetAsync(owner, hostId);

        Assert.Equal("pve", first.Node);
        Assert.Equal([100, 101], first.Guests.Select(guest => guest.Id));
        Assert.Equal("lxc", first.Guests[1].Type);
        Assert.Equal(12.5, first.Guests[0].CpuPercent);

        await agent.SendSamplesAsync(Report(DateTimeOffset.UtcNow, false, Guest(100, "running"), Guest(101, "running", "lxc")));
        var second = await GetAsync(owner, hostId);

        Assert.Equal(first.Guests[0].StatusSince, second.Guests[0].StatusSince);
        Assert.True(second.Guests[1].StatusSince > first.Guests[1].StatusSince);
    }

    [Fact]
    public async Task A_problem_keeps_the_guests_last_seen()
    {
        var (hostId, owner, agent) = await HostAsync("pve-problem@example.com", false, Guest(100, "running"));
        await agent.SendSamplesAsync(MetricsIngestionTests.FullSample(DateTimeOffset.UtcNow) with
        {
            Proxmox = new ProxmoxReport { Problem = "Proxmox VE refused the agent's API token." },
        });

        var proxmox = await GetAsync(owner, hostId);
        Assert.Equal("Proxmox VE refused the agent's API token.", proxmox.Problem);
        Assert.Equal(100, Assert.Single(proxmox.Guests).Id);
    }

    [Fact]
    public async Task Hosts_without_proxmox_report_nothing_and_others_cannot_see_it()
    {
        var owner = await app.CreateOwnerAsync("pve-none@example.com");
        var (hostId, _) = await app.RegisterHostAsync(owner, "plain-machine");
        Assert.Null((await GetAsync(owner, hostId)).CheckedAt);

        var stranger = await app.CreateOwnerAsync("pve-stranger@example.com");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/hosts/{hostId}/proxmox", Ct)).StatusCode);
    }

    [Fact]
    public async Task Actions_go_to_the_agent_with_the_guests_type()
    {
        var (hostId, owner, agent) = await HostAsync("pve-act@example.com", true, Guest(100, "stopped"), Guest(200, "running", "lxc"));
        var (loop, received, stop) = ContainerActionTests.Listen(agent, command => new AgentCommandResult
        {
            Id = command.Id,
            Succeeded = command.GuestId == 100,
            Error = command.GuestId == 100 ? null : "CT 200 is locked (backup)",
        });

        HttpResponseMessage started;
        var attempt = 0;
        do
        {
            // The agent's first request for commands may not have arrived yet.
            started = await owner.PostAsync($"/api/hosts/{hostId}/proxmox/guests/100/start", null, Ct);
            if (started.StatusCode == HttpStatusCode.Conflict)
            {
                await Task.Delay(100, Ct);
            }
        }
        while (started.StatusCode == HttpStatusCode.Conflict && ++attempt < 50);

        Assert.Equal(HttpStatusCode.NoContent, started.StatusCode);
        var refused = await owner.PostAsync($"/api/hosts/{hostId}/proxmox/guests/200/shutdown", null, Ct);
        Assert.Equal(HttpStatusCode.BadGateway, refused.StatusCode);
        Assert.Contains("locked", await refused.Content.ReadAsStringAsync(Ct));

        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsync($"/api/hosts/{hostId}/proxmox/guests/300/start", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsync($"/api/hosts/{hostId}/proxmox/guests/100/explode", null, Ct)).StatusCode);

        await stop.CancelAsync();
        await loop;
        lock (received)
        {
            Assert.Equal(
                [(AgentCommandKind.GuestStart, 100, "qemu"), (AgentCommandKind.GuestShutdown, 200, "lxc")],
                received.Select(command => (command.Kind, command.GuestId ?? 0, command.GuestType ?? "")));
        }
    }

    [Fact]
    public async Task Actions_are_refused_where_the_machine_does_not_allow_them()
    {
        var (hostId, owner, _) = await HostAsync("pve-off@example.com", false, Guest(100, "stopped"));

        var response = await owner.PostAsync($"/api/hosts/{hostId}/proxmox/guests/100/start", null, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("--proxmox-actions", await response.Content.ReadAsStringAsync(Ct));
    }
}
