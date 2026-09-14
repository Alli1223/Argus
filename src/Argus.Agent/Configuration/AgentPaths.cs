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

    public static string DefaultStateDirectory =>
        OperatingSystem.IsWindows() ? WindowsDataDirectory : "/var/lib/argus-agent";
}
