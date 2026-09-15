using System.Security.Cryptography;
using System.Text;

namespace Argus.Agent.Collection;

/// <summary>
/// Derives the stable machine identifier sent to the server. The OS identifier (e.g. /etc/machine-id)
/// is hashed with an application-specific prefix, as the systemd documentation recommends, so the raw
/// value never leaves the machine.
/// </summary>
internal static class MachineIdentity
{
    public static string Hash(string rawMachineId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("argus-machine:" + rawMachineId.Trim())));
}
