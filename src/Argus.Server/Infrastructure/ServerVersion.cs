using System.Reflection;

namespace Argus.Server.Infrastructure;

public static class ServerVersion
{
    /// <summary>The server's version, without build metadata: "1.4.0".</summary>
    public static readonly string Current =
        typeof(ServerVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";
}
