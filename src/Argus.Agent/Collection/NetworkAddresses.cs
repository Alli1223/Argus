using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Numerics;
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

    /// <summary>
    /// The machine's network cards with their hardware addresses and IPv4 networks, which the server keeps
    /// so it can wake the machine with Wake-on-LAN once it is off.
    /// </summary>
    public static List<NetworkInterfaceInfo> CollectInterfaces()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    && !IsContainerInterface(nic.Name)
                    && !IsVirtualInterface(nic.Name))
                .Select(nic => (nic, Mac: FormatMac(nic.GetPhysicalAddress().GetAddressBytes())))
                .Where(found => found.Mac is not null)
                .Select(found => new NetworkInterfaceInfo
                {
                    Name = found.nic.Name,
                    MacAddress = found.Mac!,
                    Addresses = found.nic.GetIPProperties().UnicastAddresses
                        .Where(unicast => unicast.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(unicast.Address))
                        .Select(unicast => $"{unicast.Address}/{PrefixLength(unicast)}")
                        .ToList(),
                })
                .Take(AgentLimits.MaxNetworkInterfaces)
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    /// <summary>A real hardware address as <c>aa:bb:cc:dd:ee:ff</c>; null for none, all zeros or the wrong length.</summary>
    public static string? FormatMac(byte[] bytes) =>
        bytes.Length == 6 && bytes.Any(b => b != 0) ? string.Join(':', bytes.Select(b => b.ToString("x2", CultureInfo.InvariantCulture))) : null;

    /// <summary>Virtual interfaces created by Docker and friends for every container.</summary>
    public static bool IsContainerInterface(string name) =>
        name.StartsWith("veth", StringComparison.Ordinal)
        || name.StartsWith("docker", StringComparison.Ordinal)
        || name.StartsWith("br-", StringComparison.Ordinal);

    /// <summary>Virtual machines' and VPNs' interfaces, whose addresses cannot wake the machine itself.</summary>
    private static bool IsVirtualInterface(string name) =>
        name.StartsWith("virbr", StringComparison.Ordinal)
        || name.StartsWith("vnet", StringComparison.Ordinal)
        || name.StartsWith("tap", StringComparison.Ordinal)
        || name.StartsWith("fwbr", StringComparison.Ordinal)
        || name.StartsWith("fwpr", StringComparison.Ordinal)
        || name.StartsWith("fwln", StringComparison.Ordinal)
        || name.StartsWith("lxc", StringComparison.Ordinal)
        || name.StartsWith("cni", StringComparison.Ordinal)
        || name.StartsWith("flannel", StringComparison.Ordinal)
        || name.StartsWith("tailscale", StringComparison.Ordinal)
        || name.StartsWith("zt", StringComparison.Ordinal)
        || name.StartsWith("wg", StringComparison.Ordinal);

    private static int PrefixLength(UnicastIPAddressInformation unicast)
    {
        try
        {
            return unicast.PrefixLength;
        }
        catch (PlatformNotSupportedException)
        {
            return unicast.IPv4Mask.GetAddressBytes().Sum(b => BitOperations.PopCount(b));
        }
    }
}
