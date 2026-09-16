using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Argus.Agent.Transport;
using Argus.Agent.Updates;
using Argus.Contracts.Agent;
using Microsoft.Extensions.Logging.Abstractions;

namespace Argus.Agent.Tests.Updates;

/// <summary>Runs the updater against a fake server and fake service, with shell scripts standing in for agent programs.</summary>
public sealed class UpdateApplierTests : IDisposable
{
    private const string Key = "argus_ak_test";
    private static readonly string OldAgent = "#!/bin/sh\necho 1.0.0\n";

    private readonly string _directory = Directory.CreateTempSubdirectory("argus-updater-").FullName;
    private readonly FakeServer _server = new();
    private readonly FakeService _service = new();

    private string Program => Path.Combine(_directory, "argus-agent");

    public UpdateApplierTests()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stand-in agents are shell scripts.");
        File.WriteAllText(Program, OldAgent);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(Program, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static string Agent(string version) => $"#!/bin/sh\necho {version}\n";

    private Task<int> ApplyAsync()
    {
        var client = new ArgusClient(new HttpClient(_server) { BaseAddress = new Uri("https://argus.test/") });
        return new UpdateApplier(client, _service, NullLogger.Instance, TimeSpan.Zero)
            .ApplyAsync(Key, Program, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_checked_update_replaces_the_agent_and_restarts_it()
    {
        _server.Offer("9.9.9", Agent("9.9.9"));

        Assert.Equal(0, await ApplyAsync());

        Assert.Equal(Agent("9.9.9"), File.ReadAllText(Program));
        Assert.Equal(["stop", "start", "running?"], _service.Calls);
        Assert.Null(_server.Reported);
        Assert.Equal([Program], Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task An_agent_that_does_not_keep_running_is_rolled_back()
    {
        _server.Offer("9.9.9", Agent("9.9.9"));
        _service.KeepsRunning = false;

        Assert.Equal(1, await ApplyAsync());

        Assert.Equal(OldAgent, File.ReadAllText(Program));
        Assert.Equal(["stop", "start", "running?", "stop", "start"], _service.Calls);
        Assert.False(_server.Reported!.Succeeded);
        Assert.Contains("did not keep running", _server.Reported.Error);
        Assert.Equal([Program], Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task A_download_that_claims_another_version_is_not_installed()
    {
        _server.Offer("9.9.9", Agent("8.0.0"));

        Assert.Equal(1, await ApplyAsync());

        Assert.Equal(OldAgent, File.ReadAllText(Program));
        Assert.Empty(_service.Calls);
        Assert.Equal("The downloaded agent says it is version 8.0.0, not 9.9.9.", _server.Reported!.Error);
        Assert.Equal([Program], Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task A_download_that_does_not_match_the_offer_is_thrown_away()
    {
        _server.Offer("9.9.9", Agent("9.9.9"), sha256: new string('0', 64));

        Assert.Equal(1, await ApplyAsync());

        Assert.Equal(OldAgent, File.ReadAllText(Program));
        Assert.Empty(_service.Calls);
        Assert.Contains("does not match the offered SHA-256", _server.Reported!.Error);
        Assert.Equal([Program], Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Nothing_happens_without_an_offer()
    {
        Assert.Equal(0, await ApplyAsync());

        Assert.Equal(OldAgent, File.ReadAllText(Program));
        Assert.Empty(_service.Calls);
        Assert.Equal([AgentApi.UpdateOffer], _server.Paths);
    }

    private sealed class FakeService : IServiceControl
    {
        public List<string> Calls { get; } = [];

        public bool KeepsRunning { get; set; } = true;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Calls.Add("stop");
            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Calls.Add("start");
            return Task.CompletedTask;
        }

        public Task<bool> IsRunningAsync(CancellationToken cancellationToken)
        {
            Calls.Add("running?");
            return Task.FromResult(KeepsRunning);
        }
    }

    private sealed class FakeServer : HttpMessageHandler
    {
        private AgentUpdateOffer? _offer;
        private byte[] _build = [];

        public List<string> Paths { get; } = [];

        public AgentUpdateResult? Reported { get; private set; }

        public void Offer(string version, string build, string? sha256 = null)
        {
            _build = Encoding.UTF8.GetBytes(build);
            _offer = new AgentUpdateOffer
            {
                Version = version,
                Sha256 = sha256 ?? Convert.ToHexStringLower(SHA256.HashData(_build)),
                Size = _build.Length,
            };
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(Key, request.Headers.Authorization?.Parameter);
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);

            if (path == AgentApi.UpdateOffer)
            {
                return _offer is null
                    ? new HttpResponseMessage(HttpStatusCode.NoContent)
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(_offer, AgentJsonContext.Default.AgentUpdateOffer)),
                    };
            }

            if (path == AgentApi.UpdateDownload)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_build) };
            }

            if (path == AgentApi.UpdateResult)
            {
                Reported = JsonSerializer.Deserialize(
                    await request.Content!.ReadAsStringAsync(cancellationToken), AgentJsonContext.Default.AgentUpdateResult);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
