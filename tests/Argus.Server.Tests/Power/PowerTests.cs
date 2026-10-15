using System.Net;
using System.Net.Http.Json;
using Argus.Contracts.Agent;
using Argus.Server.Features.Power;
using Argus.Server.Tests.Agents;
using Argus.Server.Tests.Containers;
using Argus.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Argus.Server.Tests.Power;

public class PowerAddressesTests
{
    [Theory]
    [InlineData("AA:BB:CC:DD:EE:FF", "aa:bb:cc:dd:ee:ff")]
    [InlineData("aa-bb-cc-dd-ee-0f", "aa:bb:cc:dd:ee:0f")]
    [InlineData(" aabbccddeeff ", "aa:bb:cc:dd:ee:ff")]
    [InlineData("00:00:00:00:00:00", null)]
    [InlineData("aa:bb:cc:dd:ee", null)]
    [InlineData("zz:bb:cc:dd:ee:ff", null)]
    [InlineData("", null)]
    public void Hardware_addresses_are_normalized(string text, string? expected) =>
        Assert.Equal(expected, PowerAddresses.NormalizeMac(text));

    [Theory]
    [InlineData("192.168.1.255", true)]
    [InlineData("192.168.1.20/24", true)]
    [InlineData("192.168.1.20/33", false)]
    [InlineData("fe80::1", false)]
    [InlineData("nas.local", false)]
    public void Wake_addresses_are_ipv4(string text, bool valid) =>
        Assert.Equal(valid, PowerAddresses.TryParseNetwork(text, out _, out _));

    [Fact]
    public void Relays_count_as_nearby_when_their_network_holds_the_machine()
    {
        Assert.True(PowerAddresses.ShareNetwork(["192.168.1.5/24"], ["192.168.1.20/24"]));
        Assert.True(PowerAddresses.ShareNetwork(["10.0.0.2/8", "192.168.1.5/24"], ["192.168.1.255"]));
        Assert.False(PowerAddresses.ShareNetwork(["192.168.2.5/24"], ["192.168.1.20/24"]));
        Assert.False(PowerAddresses.ShareNetwork(["192.168.1.5"], ["192.168.1.20/24"]));
        Assert.False(PowerAddresses.ShareNetwork(["192.168.1.5/24"], []));
    }
}

public sealed class PowerTests(ArgusAppFixture app) : IClassFixture<ArgusAppFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static NetworkInterfaceInfo Nic(string mac, params string[] addresses) =>
        new() { Name = "eth0", MacAddress = mac, Addresses = addresses };

    private static async Task ReportAsync(HttpClient agent, AgentCommandKind[] commands, params NetworkInterfaceInfo[] interfaces)
    {
        var report = new InventoryReport
        {
            AgentVersion = "1.0.0",
            SystemInfo = AgentRegistrationTests.Registration("", "").SystemInfo with { NetworkInterfaces = interfaces },
            Commands = commands,
        };
        (await agent.PutAsJsonAsync(AgentApi.Inventory, report, AgentJsonContext.Default.InventoryReport, Ct)).EnsureSuccessStatusCode();
    }

    private static async Task WaitUntilListeningAsync(HttpClient owner, Guid hostId)
    {
        for (var attempt = 0; attempt < 50 && !(await owner.GetJsonAsync<HostPower>($"/api/hosts/{hostId}/power"))!.Listening; attempt++)
        {
            await Task.Delay(100, Ct);
        }
    }

    [Fact]
    public async Task Shutting_down_is_refused_unless_the_machine_allows_it()
    {
        var owner = await app.CreateOwnerAsync("power-a@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "power-a-machine");
        await ReportAsync(agent, [AgentCommandKind.WakeOnLan]);

        var response = await owner.PostAsync($"/api/hosts/{hostId}/power/shutdown", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        Assert.Equal("Power actions are off on this machine", problem.Title);

        var power = (await owner.GetJsonAsync<HostPower>($"/api/hosts/{hostId}/power"))!;
        Assert.False(power.PowerActionsEnabled);
        var attempt = Assert.Single(power.History);
        Assert.Equal((PowerActionKind.Shutdown, false, "power-a@example.com"), (attempt.Kind, attempt.Succeeded, attempt.RequestedBy));

        var stranger = await app.CreateOwnerAsync("power-a-stranger@example.com");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"/api/hosts/{hostId}/power/shutdown", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/hosts/{hostId}/power", Ct)).StatusCode);
    }

    [Fact]
    public async Task Owners_shut_down_and_restart_machines_through_their_agent()
    {
        var owner = await app.CreateOwnerAsync("power-b@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "power-b-machine");
        await ReportAsync(agent, [AgentCommandKind.WakeOnLan, AgentCommandKind.Shutdown, AgentCommandKind.Restart]);
        var (loop, received, stop) = ContainerActionTests.Listen(agent, command => command.Kind == AgentCommandKind.Restart
            ? new AgentCommandResult { Id = command.Id, Succeeded = false, Error = "The machine refused the request." }
            : new AgentCommandResult { Id = command.Id, Succeeded = true });
        await WaitUntilListeningAsync(owner, hostId);

        var shutdown = await owner.PostAsync($"/api/hosts/{hostId}/power/shutdown", null, Ct);
        Assert.Equal(HttpStatusCode.OK, shutdown.StatusCode);
        var restart = await owner.PostAsync($"/api/hosts/{hostId}/power/restart", null, Ct);
        Assert.Equal(HttpStatusCode.BadGateway, restart.StatusCode);
        Assert.Equal("The machine refused the request.", (await restart.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Detail);

        await stop.CancelAsync();
        await loop;
        Assert.Equal([AgentCommandKind.Shutdown, AgentCommandKind.Restart], received.Select(command => command.Kind));
        Assert.Contains("power-b@example.com", received[0].Reason);

        var power = (await owner.GetJsonAsync<HostPower>($"/api/hosts/{hostId}/power"))!;
        Assert.True(power.PowerActionsEnabled);
        Assert.Equal([(PowerActionKind.Restart, false), (PowerActionKind.Shutdown, true)], power.History.Select(action => (action.Kind, action.Succeeded)));
    }

    [Fact]
    public async Task Machines_are_woken_by_an_agent_on_their_network()
    {
        var owner = await app.CreateOwnerAsync("power-c@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "power-c-desktop", "desktop");
        await ReportAsync(agent, [AgentCommandKind.WakeOnLan], Nic("AA:BB:CC:DD:EE:01", "192.168.1.20/24"));
        var (farId, far) = await app.RegisterHostAsync(owner, "power-c-cloud", "cloud");
        await ReportAsync(far, [AgentCommandKind.WakeOnLan], Nic("aa:bb:cc:dd:ee:02", "10.8.0.4/24"));
        var (nasId, nas) = await app.RegisterHostAsync(owner, "power-c-nas", "nas");
        await ReportAsync(nas, [AgentCommandKind.WakeOnLan], Nic("aa:bb:cc:dd:ee:03", "192.168.1.5/24"));
        var (farLoop, farReceived, farStop) = ContainerActionTests.Listen(far, command => new AgentCommandResult { Id = command.Id, Succeeded = true });
        var (nasLoop, nasReceived, nasStop) = ContainerActionTests.Listen(nas, command => new AgentCommandResult { Id = command.Id, Succeeded = true });
        await WaitUntilListeningAsync(owner, farId);
        await WaitUntilListeningAsync(owner, nasId);

        var power = (await owner.GetJsonAsync<HostPower>($"/api/hosts/{hostId}/power"))!;
        Assert.Equal(["aa:bb:cc:dd:ee:01"], power.MacAddresses);
        Assert.Equal(1, power.WakeRelays);

        var woken = await owner.PostAsync($"/api/hosts/{hostId}/power/wake", null, Ct);
        Assert.Equal(HttpStatusCode.OK, woken.StatusCode);
        Assert.Equal("Sent by nas", (await woken.Content.ReadFromJsonAsync<PowerActionInfo>(TestJson.Options, Ct))!.Detail);

        // Settings can name another address to wake by and where to send the packet.
        var settings = await owner.PutAsJsonAsync($"/api/hosts/{hostId}/power/settings",
            new PowerSettingsRequest { WakeMacAddress = "AA-BB-CC-DD-EE-99", WakeAddress = "192.168.1.255" }, Ct);
        Assert.Equal("aa:bb:cc:dd:ee:99", (await settings.Content.ReadFromJsonAsync<HostPower>(TestJson.Options, Ct))!.WakeMacAddress);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"/api/hosts/{hostId}/power/wake", null, Ct)).StatusCode);

        await farStop.CancelAsync();
        await nasStop.CancelAsync();
        await Task.WhenAll(farLoop, nasLoop);
        Assert.Empty(farReceived);
        Assert.Equal(2, nasReceived.Count);
        Assert.All(nasReceived, command => Assert.Equal(AgentCommandKind.WakeOnLan, command.Kind));
        Assert.Equal(["aa:bb:cc:dd:ee:01"], nasReceived[0].MacAddresses!);
        Assert.Equal(["192.168.1.20/24"], nasReceived[0].Addresses!);
        Assert.Equal(["aa:bb:cc:dd:ee:99"], nasReceived[1].MacAddresses!);
        Assert.Equal(["192.168.1.255", "192.168.1.20/24"], nasReceived[1].Addresses!);
    }

    [Fact]
    public async Task Waking_needs_a_hardware_address_and_another_connected_agent()
    {
        var owner = await app.CreateOwnerAsync("power-d@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "power-d-machine");
        await ReportAsync(agent, [AgentCommandKind.WakeOnLan]);

        var response = await owner.PostAsync($"/api/hosts/{hostId}/power/wake", null, Ct);
        Assert.Equal("No hardware address to wake", (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Title);

        var invalid = await owner.PutAsJsonAsync($"/api/hosts/{hostId}/power/settings",
            new PowerSettingsRequest { WakeMacAddress = "not a mac", WakeAddress = "nas.local" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = (await invalid.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct))!.Errors;
        Assert.Equal(["wakeAddress", "wakeMacAddress"], errors.Keys.Order());

        (await owner.PutAsJsonAsync($"/api/hosts/{hostId}/power/settings", new PowerSettingsRequest { WakeMacAddress = "aa:bb:cc:dd:ee:ff" }, Ct))
            .EnsureSuccessStatusCode();
        response = await owner.PostAsync($"/api/hosts/{hostId}/power/wake", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Nothing to send the packet", (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Title);
    }
}
