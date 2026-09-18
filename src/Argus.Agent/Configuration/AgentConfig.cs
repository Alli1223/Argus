namespace Argus.Agent.Configuration;

/// <summary>
/// Agent settings, read from the config file (see <see cref="AgentPaths"/>) and from environment
/// variables prefixed with <c>ARGUS_</c> (e.g. <c>ARGUS_SERVERURL</c>, <c>ARGUS_ENROLLMENTTOKEN</c>).
/// </summary>
public sealed class AgentConfig
{
    /// <summary>Base URL of the Argus server, e.g. <c>https://argus.example.com</c>.</summary>
    public string ServerUrl { get; set; } = "";

    /// <summary>Enrollment token from the web UI; only needed until the agent has registered.</summary>
    public string? EnrollmentToken { get; set; }

    /// <summary>Where the agent keeps its identity (host id and agent key).</summary>
    public string? StateDirectory { get; set; }

    /// <summary>Overrides the collection interval the server asks for.</summary>
    public int? CollectionIntervalSeconds { get; set; }

    /// <summary>Samples kept while the server is unreachable (default: 12 hours at 15 s).</summary>
    public int BufferCapacity { get; set; } = 2880;

    /// <summary>
    /// Also reads SATA drive temperatures (the Linux drivetemp driver). Off by default: on some drives,
    /// reading the temperature resets the spin-down timer, so drives meant to sleep stay awake.
    /// </summary>
    public bool DriveTemperatures { get; set; }

    /// <summary>Docker's socket. The agent watches containers when it exists and the agent may use it.</summary>
    public string DockerSocket { get; set; } = "/var/run/docker.sock";

    /// <summary>
    /// Where the machine's own filesystem is mounted when the agent runs in a container, such as
    /// <c>/host</c>. Empty when the agent runs on the machine itself, which is the usual case.
    /// </summary>
    public string? HostRoot { get; set; }

    /// <summary>
    /// Lets people who can see this machine in Argus read its containers' logs and start, stop and restart
    /// them. Off by default, so that an Argus login alone never controls a machine's containers.
    /// </summary>
    public bool ContainerActions { get; set; }

    public string ResolvedStateDirectory =>
        string.IsNullOrWhiteSpace(StateDirectory) ? AgentPaths.DefaultStateDirectory : StateDirectory;

    public Uri ServerUri => new(ServerUrl.TrimEnd('/') + "/");

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            errors.Add("ServerUrl must be an absolute http(s) URL, e.g. https://argus.example.com.");
        }

        if (CollectionIntervalSeconds is < 5 or > 3600)
        {
            errors.Add("CollectionIntervalSeconds must be between 5 and 3600.");
        }

        if (BufferCapacity is < 10 or > 100_000)
        {
            errors.Add("BufferCapacity must be between 10 and 100000.");
        }

        return errors;
    }

    /// <summary>True when the agent key would travel unencrypted to a remote machine.</summary>
    public bool UsesPlainHttpToRemoteServer =>
        Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback;
}
