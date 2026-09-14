using System.Reflection;
using System.Runtime.InteropServices;
using Argus.Contracts.Agent;

namespace Argus.Agent;

internal static class AgentInfo
{
    public static string Version { get; } =
        typeof(AgentInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    public static HostPlatform Platform { get; } =
        OperatingSystem.IsLinux() ? HostPlatform.Linux
        : OperatingSystem.IsWindows() ? HostPlatform.Windows
        : OperatingSystem.IsMacOS() ? HostPlatform.MacOS
        : HostPlatform.Unknown;

    /// <summary>Processor architecture in the form the server shows ("x64", "arm64").</summary>
    public static string Architecture { get; } = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();

    public static string UserAgent { get; } =
        $"argus-agent/{Version} ({Platform.ToString().ToLowerInvariant()}; {Architecture})";
}
