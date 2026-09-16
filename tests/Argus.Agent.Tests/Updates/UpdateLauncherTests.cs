using Argus.Agent.Updates;
using Argus.Contracts.Agent;

namespace Argus.Agent.Tests.Updates;

public sealed class UpdateLauncherTests : IDisposable
{
    private static readonly AgentUpdateOffer Offer = new() { Version = "9.9.9", Sha256 = new string('a', 64), Size = 10 };

    private readonly string _directory = Directory.CreateTempSubdirectory("argus-launcher-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void On_Linux_the_agent_leaves_a_request_for_the_root_updater()
    {
        var unit = Path.Combine(_directory, "argus-agent-update.path");
        var request = Path.Combine(_directory, "update-requested");
        File.WriteAllText(unit, "[Path]");

        Assert.Null(new SystemdUpdateLauncher(unit, request).Launch(Offer));
        Assert.Equal("9.9.9", File.ReadAllText(request));
    }

    [Fact]
    public void Agents_installed_without_the_updater_say_how_to_add_it()
    {
        var request = Path.Combine(_directory, "update-requested");

        var problem = new SystemdUpdateLauncher(Path.Combine(_directory, "missing.path"), request).Launch(Offer);

        Assert.Equal("This agent was installed without its updater. Run the install command on the machine once more to add it.", problem);
        Assert.False(File.Exists(request));
    }

    [Fact]
    public void Agents_not_running_as_a_service_cannot_update_themselves() =>
        Assert.NotNull(new NoUpdateLauncher().Launch(Offer));
}
