using Argus.Agent.Collection.Linux;

namespace Argus.Agent.Tests.Collection;

/// <summary>Where the agent looks for the machine's files, on the machine itself and from a container.</summary>
public class HostPathsTests
{
    [Fact]
    public void An_agent_on_the_machine_reads_the_usual_places()
    {
        var paths = HostPaths.Local;

        Assert.False(paths.Containerized);
        Assert.Equal(("/proc", "/sys", "/etc"), (paths.Proc, paths.Sys, paths.Etc));
        Assert.Equal("/proc/meminfo", paths.InProc("meminfo"));

        // Its own mount table and network are the machine's.
        Assert.Equal("/proc/self/mounts", paths.Mounts);
        Assert.Equal("/proc/net/dev", paths.NetDev);
        Assert.Equal("/", paths.Mounted("/"));
        Assert.Equal("/mnt/data", paths.Mounted("/mnt/data"));
    }

    [Theory]
    [InlineData("/host")]
    [InlineData("/host/")]
    public void An_agent_in_a_container_reads_them_under_the_root_it_is_given(string root)
    {
        var paths = new HostPaths(root);

        Assert.True(paths.Containerized);
        Assert.Equal(("/host/proc", "/host/sys", "/host/etc"), (paths.Proc, paths.Sys, paths.Etc));
        Assert.Equal("/host/proc/meminfo", paths.InProc("meminfo"));

        // The agent's own mount table and network are the container's, so the host's first process answers instead.
        Assert.Equal("/host/proc/1/mounts", paths.Mounts);
        Assert.Equal("/host/proc/1/net/dev", paths.NetDev);

        // Measuring a filesystem means measuring it where it is mounted in here.
        Assert.Equal("/host", paths.Mounted("/"));
        Assert.Equal("/host/mnt/data", paths.Mounted("/mnt/data"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_root_means_the_machine_itself(string? root) => Assert.False(new HostPaths(root).Containerized);
}
