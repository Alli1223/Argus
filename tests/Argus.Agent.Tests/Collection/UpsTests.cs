using System.Net;
using System.Net.Sockets;
using System.Text;
using Argus.Agent.Collection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Argus.Agent.Tests.Collection;

public sealed class UpsTests
{
    [Theory]
    [InlineData("localhost", "localhost", 3493)]
    [InlineData("nas.lan:3500", "nas.lan", 3500)]
    [InlineData("192.168.1.10", "192.168.1.10", 3493)]
    [InlineData("[::1]:3500", "::1", 3500)]
    [InlineData("[::1]", "::1", 3493)]
    [InlineData("::1", "::1", 3493)]
    public void Servers_are_a_host_and_an_optional_port(string server, string host, int port) =>
        Assert.Equal((host, port), NutProtocol.ParseServer(server));

    [Fact]
    public void Quoted_words_keep_their_spaces_and_escapes() =>
        Assert.Equal(["VAR", "ups", "ups.mfr", "Say \"hi\" \\ bye"], NutProtocol.Split("""VAR ups ups.mfr "Say \"hi\" \\ bye" """));

    [Fact]
    public void Variables_become_readings()
    {
        var variables = NutProtocol.ParseVariables(
        [
            "VAR ups battery.charge \"16\"",
            "VAR ups battery.runtime \"420\"",
            "VAR ups battery.voltage \"12.4\"",
            "VAR ups device.mfr \"American Power Conversion\"",
            "VAR ups device.model \"Back-UPS ES 700G\"",
            "VAR ups input.voltage \"0.0\"",
            "VAR ups ups.load \"23\"",
            "VAR ups ups.status \"OB DISCHRG\"",
        ]);

        var ups = NutProtocol.ToMetrics("ups", "Rack UPS", variables);

        Assert.Equal("ups", ups.Name);
        Assert.Equal("American Power Conversion Back-UPS ES 700G", ups.Model);
        Assert.Equal("OB DISCHRG", ups.Status);
        Assert.Equal(16, ups.BatteryChargePercent);
        Assert.Equal(420, ups.BatteryRuntimeSeconds);
        Assert.Equal(23, ups.LoadPercent);
        Assert.Equal(0, ups.InputVoltage);
        Assert.Equal(12.4, ups.BatteryVoltage);
        Assert.Null(ups.OutputVoltage);
    }

    [Fact]
    public void Without_a_model_the_description_names_the_ups()
    {
        var ups = NutProtocol.ToMetrics("ups", "Rack UPS", new Dictionary<string, string> { ["ups.status"] = "OL", ["battery.charge"] = "n/a" });

        Assert.Equal("Rack UPS", ups.Model);
        Assert.Null(ups.BatteryChargePercent);
    }

    [Fact]
    public async Task Reads_every_ups_upsd_lists()
    {
        await using var server = FakeUpsd.Start(new Dictionary<string, string[]>
        {
            ["LIST UPS"] = ["BEGIN LIST UPS", "UPS ups \"Rack UPS\"", "UPS spare \"Unavailable\"", "UPS gone \"\"", "END LIST UPS"],
            ["LIST VAR ups"] = ["BEGIN LIST VAR ups", "VAR ups ups.status \"OL CHRG\"", "VAR ups battery.charge \"87\"", "END LIST VAR ups"],
            ["LIST VAR spare"] = ["BEGIN LIST VAR spare", "VAR spare ups.status \"OL\"", "END LIST VAR spare"],
            ["LIST VAR gone"] = ["ERR DRIVER-NOT-CONNECTED"],
        });
        var source = new NutUps($"127.0.0.1:{server.Port}", TimeProvider.System, NullLogger<NutUps>.Instance);

        var readings = source.Collect();

        Assert.Collection(
            readings,
            ups =>
            {
                Assert.Equal("ups", ups.Name);
                Assert.Equal("OL CHRG", ups.Status);
                Assert.Equal(87, ups.BatteryChargePercent);
            },
            spare =>
            {
                Assert.Equal("spare", spare.Name);
                Assert.Null(spare.Model);
            });
    }

    [Fact]
    public void Without_upsd_it_reports_none_and_waits_before_asking_again()
    {
        // A port that was just free: nothing listens there.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var source = new NutUps($"127.0.0.1:{port}", time, NullLogger<NutUps>.Instance);

        Assert.Empty(source.Collect());
        Assert.Empty(source.Collect());
        time.Advance(NutUps.RetryInterval);
        Assert.Empty(source.Collect());
    }

    /// <summary>Answers each command with fixed lines, as upsd would.</summary>
    private sealed class FakeUpsd : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private Task _serving = Task.CompletedTask;

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public static FakeUpsd Start(IReadOnlyDictionary<string, string[]> answers)
        {
            var server = new FakeUpsd();
            server._listener.Start();
            server._serving = server.ServeAsync(answers);
            return server;
        }

        private async Task ServeAsync(IReadOnlyDictionary<string, string[]> answers)
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    await using var writer = new StreamWriter(stream) { NewLine = "\n", AutoFlush = true };
                    while (await reader.ReadLineAsync(_stop.Token) is { } command && command != "LOGOUT")
                    {
                        foreach (var line in answers.GetValueOrDefault(command, ["ERR UNKNOWN-COMMAND"]))
                        {
                            await writer.WriteLineAsync(line);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            await _serving;
            _stop.Dispose();
        }
    }
}
