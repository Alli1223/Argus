namespace Argus.Agent.Collection.Linux;

/// <summary>
/// Where the machine's own <c>/proc</c>, <c>/sys</c> and filesystems are. They are in the usual places
/// for an agent installed on the machine. In a container they are the host's, bind-mounted under a root
/// such as <c>/host</c>, and the files that belong to a namespace (the mount table and network counters)
/// are read through the host's first process rather than the agent's own.
/// </summary>
internal sealed class HostPaths
{
    /// <summary>The machine the agent runs on, read directly.</summary>
    public static readonly HostPaths Local = new(null);

    public HostPaths(string? root)
    {
        Root = string.IsNullOrWhiteSpace(root) ? "" : root.TrimEnd('/');
        Containerized = Root.Length > 0;
        Proc = Root + "/proc";
        Sys = Root + "/sys";
        Etc = Root + "/etc";
        Mounts = Containerized ? $"{Proc}/1/mounts" : "/proc/self/mounts";
        NetDev = Containerized ? $"{Proc}/1/net/dev" : "/proc/net/dev";
    }

    /// <summary>True when the agent watches a machine from a container, through <see cref="Root"/>.</summary>
    public bool Containerized { get; }

    /// <summary>Where the machine's root filesystem is, or empty when the agent runs on the machine.</summary>
    public string Root { get; }

    public string Proc { get; }

    public string Sys { get; }

    public string Etc { get; }

    /// <summary>The mount table of the machine, not of the container the agent may be in.</summary>
    public string Mounts { get; }

    /// <summary>Network counters for the machine's own network, not the container's.</summary>
    public string NetDev { get; }

    /// <summary>Where a file the machine has at <paramref name="path"/> can be read.</summary>
    public string InProc(string path) => $"{Proc}/{path}";

    /// <summary>Where a mount point of the machine can be measured from here.</summary>
    public string Mounted(string mountPoint) =>
        !Containerized ? mountPoint : mountPoint == "/" ? Root : Root + mountPoint;
}
