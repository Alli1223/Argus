using Argus.Agent.Configuration;
using Argus.Agent.State;

namespace Argus.Agent.Tests.State;

public sealed class StateStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "argus-agent-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void State_round_trips_and_is_private()
    {
        var store = new StateStore(Path.Combine(_directory, "state"));
        Assert.Null(store.Load());

        var state = new AgentState
        {
            HostId = Guid.NewGuid(),
            AgentKey = "argus_ak_secret",
            ServerUrl = "https://argus.example.com",
            RegisteredAt = DateTimeOffset.UtcNow,
        };
        store.Save(state);

        Assert.Equal(state, store.Load());
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(store.FilePath));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(store.Directory));
        }
    }

    [Fact]
    public void Corrupt_state_is_treated_as_unregistered()
    {
        var store = new StateStore(_directory);
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.FilePath, "{ not json");

        Assert.Null(store.Load());
    }

    [Fact]
    public void Generated_values_are_created_once()
    {
        var store = new StateStore(_directory);

        var first = store.GetOrCreate("machine-id", () => "generated-1");
        var second = store.GetOrCreate("machine-id", () => "generated-2");

        Assert.Equal("generated-1", first);
        Assert.Equal("generated-1", second);
    }

    [Theory]
    [InlineData("https://argus.example.com", true)]
    [InlineData("http://localhost:5080", true)]
    [InlineData("ftp://argus.example.com", false)]
    [InlineData("argus.example.com", false)]
    [InlineData("", false)]
    public void Config_requires_an_http_server_url(string url, bool valid)
    {
        var config = new AgentConfig { ServerUrl = url };

        Assert.Equal(valid, config.Validate().Count == 0);
    }

    [Fact]
    public void Plain_http_to_a_remote_server_is_flagged()
    {
        Assert.True(new AgentConfig { ServerUrl = "http://argus.example.com" }.UsesPlainHttpToRemoteServer);
        Assert.False(new AgentConfig { ServerUrl = "http://127.0.0.1:5080" }.UsesPlainHttpToRemoteServer);
        Assert.False(new AgentConfig { ServerUrl = "https://argus.example.com" }.UsesPlainHttpToRemoteServer);
    }
}
