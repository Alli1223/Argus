namespace Argus.Agent.Configuration;

/// <summary>Default locations of the agent's files on each platform.</summary>
public static class AgentPaths
{
    /// <summary>Environment variable that points the agent at a different config file.</summary>
    public const string ConfigFileVariable = "ARGUS_CONFIG";

    private static string WindowsDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Argus", "Agent");

    public static string DefaultConfigFile =>
        Environment.GetEnvironmentVariable(ConfigFileVariable) is { Length: > 0 } fromEnvironment
            ? fromEnvironment
            : OperatingSystem.IsWindows()
                ? Path.Combine(WindowsDataDirectory, "agent.json")
                : "/etc/argus-agent/agent.json";

    /// <summary>
    /// On Linux, the agent asks for an update by creating this file; a root systemd unit watching for it
    /// installs the update. It sits in the agent's own state directory, the one place the service may write.
    /// </summary>
    public const string LinuxUpdateRequestFile = "/var/lib/argus-agent/update-requested";

    /// <summary>The systemd unit that watches for update requests, which install.sh adds.</summary>
    public const string LinuxUpdaterUnit = "/etc/systemd/system/argus-agent-update.path";

    public static string DefaultStateDirectory =>
        OperatingSystem.IsWindows() ? WindowsDataDirectory : "/var/lib/argus-agent";
}
