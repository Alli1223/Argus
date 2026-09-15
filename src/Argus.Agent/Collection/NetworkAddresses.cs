using System.Net;
using System.Net.NetworkInformation;
using Argus.Contracts.Agent;

namespace Argus.Agent.Collection;

internal static class NetworkAddresses
{
    /// <summary>The machine's routable addresses, skipping loopback, link-local and container plumbing.</summary>
    public static List<string> Collect()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                    && nic.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    && !IsContainerInterface(nic.Name))
                .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                .Select(unicast => unicast.Address)
                .Where(address => !IPAddress.IsLoopback(address) && !address.IsIPv6LinkLocal)
                .Select(address => address.ToString())
                .Distinct()
                .Take(AgentLimits.MaxIpAddresses)
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    /// <summary>Virtual interfaces created by Docker and friends for every container.</summary>
    public static bool IsContainerInterface(string name) =>
        name.StartsWith("veth", StringComparison.Ordinal)
        || name.StartsWith("docker", StringComparison.Ordinal)
        || name.StartsWith("br-", StringComparison.Ordinal);
}
