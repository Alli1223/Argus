using System.Buffers.Binary;
using System.Net;
using Argus.Agent.Collection;
using Argus.Agent.Commands;
using Argus.Agent.Configuration;
using Argus.Contracts.Agent;
using Microsoft.Extensions.Logging.Abstractions;

namespace Argus.Agent.Tests.Commands;

public sealed class PowerCommandsTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _directory = Directory.CreateTempSubdirectory("argus-power-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class FakeControl(string? error = null) : IPowerControl
    {
        public List<(PowerAction Action, string Reason)> Requests { get; } = [];

        public Task<string?> RequestAsync(PowerAction action, string reason, CancellationToken cancellationToken)
        {
            Requests.Add((action, reason));
            return Task.FromResult(error);
        }
    }

    private static AgentCommand Command(AgentCommandKind kind, string? reason = null) => new() { Id = "p1", Kind = kind, Reason = reason };

    [Fact]
    public async Task Machines_that_do_not_allow_power_actions_refuse_them()
    {
        var control = new FakeControl();
        var commands = new PowerCommands(new AgentConfig { PowerActions = false }, control, NullLogger<PowerCommands>.Instance);

        var result = await commands.RunAsync(Command(AgentCommandKind.Shutdown), Ct);

        Assert.False(result.Succeeded);
        Assert.Equal("Shutting down and restarting are off on this machine.", result.Error);
        Assert.Empty(control.Requests);
    }

    [Fact]
    public async Task Shutdown_and_restart_ask_the_machine_and_pass_on_its_refusal()
    {
        var control = new FakeControl();
        var commands = new PowerCommands(new AgentConfig { PowerActions = true }, control, NullLogger<PowerCommands>.Instance);

        Assert.True((await commands.RunAsync(Command(AgentCommandKind.Shutdown, "UPS ups on nas is on battery"), Ct)).Succeeded);
        Assert.True((await commands.RunAsync(Command(AgentCommandKind.Restart), Ct)).Succeeded);
        Assert.Equal([(PowerAction.PowerOff, "UPS ups on nas is on battery"), (PowerAction.Reboot, "Asked through Argus")], control.Requests);

        var refusing = new PowerCommands(new AgentConfig { PowerActions = true }, new FakeControl("No."), NullLogger<PowerCommands>.Instance);
        Assert.Equal("No.", (await refusing.RunAsync(Command(AgentCommandKind.Shutdown), Ct)).Error);
    }

    [Fact]
    public async Task The_installed_agent_leaves_a_request_for_its_power_unit()
    {
        var unit = Path.Combine(_directory, "argus-agent-power.path");
        var request = Path.Combine(_directory, "power-requested");
        var control = new SystemdPowerRequest(unit, request);

        Assert.Contains("--power-actions", await control.RequestAsync(PowerAction.PowerOff, "test", Ct));
        Assert.False(File.Exists(request));

        await File.WriteAllTextAsync(unit, "", Ct);
        Assert.Null(await control.RequestAsync(PowerAction.Reboot, "test", Ct));
        Assert.Equal(PowerAction.Reboot, PowerRequests.Parse(await File.ReadAllTextAsync(request, Ct)));
        Assert.Null(PowerRequests.Parse("halt"));
    }

    [Fact]
    public void SysV_init_is_asked_to_change_runlevel()
    {
        var request = PowerRequests.Initctl(PowerAction.PowerOff);

        Assert.Equal(384, request.Length);
        Assert.Equal(0x03091969, BinaryPrimitives.ReadInt32LittleEndian(request));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(request.AsSpan(4)));
        Assert.Equal('0', BinaryPrimitives.ReadInt32LittleEndian(request.AsSpan(8)));
        Assert.Equal('6', BinaryPrimitives.ReadInt32LittleEndian(PowerRequests.Initctl(PowerAction.Reboot).AsSpan(8)));
    }

    [Fact]
    public void Agents_say_which_commands_their_settings_allow()
    {
        Assert.Equal([AgentCommandKind.WakeOnLan], CommandWorker.Supported(new AgentConfig()));

        var everything = CommandWorker.Supported(new AgentConfig { ContainerActions = true, PowerActions = true });
        Assert.Contains(AgentCommandKind.Shutdown, everything);
        Assert.Contains(AgentCommandKind.Restart, everything);
        Assert.Equal(OperatingSystem.IsLinux(), everything.Contains(AgentCommandKind.ContainerStop));
    }
}

public sealed class WakeOnLanTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_magic_packet_is_six_bytes_of_ff_then_the_address_sixteen_times()
    {
        var mac = WakeOnLanPackets.ParseMac("AA-BB-CC-DD-EE-FF")!;
        var packet = WakeOnLanPackets.MagicPacket(mac);

        Assert.Equal(102, packet.Length);
        Assert.All(packet[..6], b => Assert.Equal(0xFF, b));
        for (var i = 0; i < 16; i++)
        {
            Assert.Equal(mac, packet[(6 + i * 6)..(12 + i * 6)]);
        }
    }

    [Theory]
    [InlineData("aa:bb:cc:dd:ee:ff", true)]
    [InlineData("aabbccddeeff", true)]
    [InlineData("aa:bb:cc:dd:ee", false)]
    [InlineData("gg:bb:cc:dd:ee:ff", false)]
    public void Hardware_addresses_are_parsed(string text, bool valid) =>
        Assert.Equal(valid, WakeOnLanPackets.ParseMac(text) is not null);

    [Fact]
    public void Packets_go_to_every_broadcast_address_that_may_reach_the_machine()
    {
        var local = new List<(IPAddress, int)> { (IPAddress.Parse("192.168.1.5"), 24), (IPAddress.Parse("10.0.0.2"), 16) };

        var destinations = WakeOnLanPackets.Destinations(["192.168.1.20/24", "10.0.7.9", "172.16.3.4", "192.168.50.255", "fe80::1", "junk"], local);

        Assert.Equal(
            ["255.255.255.255", "192.168.1.255", "10.0.255.255", "172.16.3.255", "192.168.50.255"],
            destinations.Select(address => address.ToString()));
    }

    [Fact]
    public void Networks_without_room_for_a_broadcast_have_none()
    {
        Assert.Null(WakeOnLanPackets.Broadcast(IPAddress.Parse("10.0.0.1"), 31));
        Assert.Null(WakeOnLanPackets.Broadcast(IPAddress.Parse("10.0.0.1"), 32));
        Assert.Equal(IPAddress.Parse("10.0.0.3"), WakeOnLanPackets.Broadcast(IPAddress.Parse("10.0.0.1"), 30));
    }

    [Fact]
    public async Task Waking_needs_a_hardware_address()
    {
        var wake = new WakeOnLan(NullLogger<WakeOnLan>.Instance);

        var result = await wake.RunAsync(new AgentCommand { Id = "w1", Kind = AgentCommandKind.WakeOnLan, MacAddresses = ["nonsense"] }, Ct);

        Assert.False(result.Succeeded);
        Assert.Equal("No hardware address to wake.", result.Error);
    }

    [Theory]
    [InlineData(new byte[] { 0xAA, 0xBB, 0xCC, 0x01, 0x02, 0x03 }, "aa:bb:cc:01:02:03")]
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 0 }, null)]
    [InlineData(new byte[] { }, null)]
    public void Interfaces_report_real_hardware_addresses(byte[] bytes, string? expected) =>
        Assert.Equal(expected, NetworkAddresses.FormatMac(bytes));
}
