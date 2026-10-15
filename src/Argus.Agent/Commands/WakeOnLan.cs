using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Argus.Agent.Collection;
using Argus.Contracts.Agent;

namespace Argus.Agent.Commands;

/// <summary>
/// Sends Wake-on-LAN packets for the server, which usually sits behind Docker's network where its
/// broadcasts never reach the machines it would wake. Any agent may do this: the packet only asks a
/// sleeping network card to start its machine.
/// </summary>
internal sealed class WakeOnLan(ILogger<WakeOnLan> logger)
{
    public const int Port = 9;

    public async Task<AgentCommandResult> RunAsync(AgentCommand command, CancellationToken cancellationToken)
    {
        var macs = (command.MacAddresses ?? []).Select(WakeOnLanPackets.ParseMac).OfType<byte[]>().Distinct(ByteArrays.Comparer).ToList();
        if (macs.Count == 0)
        {
            return new AgentCommandResult { Id = command.Id, Succeeded = false, Error = "No hardware address to wake." };
        }

        var targets = WakeOnLanPackets.Destinations(command.Addresses ?? [], LocalNetworks());
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { EnableBroadcast = true };
        var sent = 0;
        string? lastError = null;
        foreach (var mac in macs)
        {
            var packet = WakeOnLanPackets.MagicPacket(mac);
            foreach (var target in targets)
            {
                try
                {
                    await socket.SendToAsync(packet, SocketFlags.None, new IPEndPoint(target, Port), cancellationToken);
                    sent++;
                }
                catch (SocketException ex)
                {
                    // One network can refuse (no route to it); the others may still carry it.
                    lastError = ex.Message;
                }
            }
        }

        if (sent == 0)
        {
            return new AgentCommandResult { Id = command.Id, Succeeded = false, Error = $"Could not send the packet: {lastError}" };
        }

        logger.LogInformation("Sent Wake-on-LAN for {Macs} to {Targets}, as asked through Argus",
            string.Join(", ", command.MacAddresses ?? []), string.Join(", ", targets));
        return new AgentCommandResult { Id = command.Id, Succeeded = true };
    }

    /// <summary>This machine's IPv4 networks, whose broadcast addresses reach the machines beside it.</summary>
    private static List<(IPAddress Address, int PrefixLength)> LocalNetworks()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                    && nic.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    && !NetworkAddresses.IsContainerInterface(nic.Name))
                .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                .Where(unicast => unicast.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(unicast.Address))
                .Select(unicast => (unicast.Address, unicast.PrefixLength))
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    private sealed class ByteArrays : IEqualityComparer<byte[]>
    {
        public static readonly ByteArrays Comparer = new();

        public bool Equals(byte[]? x, byte[]? y) => x is not null && y is not null && x.AsSpan().SequenceEqual(y);

        public int GetHashCode(byte[] bytes) => Convert.ToHexString(bytes).GetHashCode(StringComparison.Ordinal);
    }
}

/// <summary>The packet and where to send it, kept free of I/O so they are easy to test.</summary>
internal static class WakeOnLanPackets
{
    /// <summary>Six bytes of 0xFF, then the hardware address sixteen times.</summary>
    public static byte[] MagicPacket(byte[] mac)
    {
        var packet = new byte[6 + 16 * 6];
        packet.AsSpan(0, 6).Fill(0xFF);
        for (var i = 0; i < 16; i++)
        {
            mac.CopyTo(packet, 6 + i * 6);
        }

        return packet;
    }

    /// <summary>"aa:bb:cc:dd:ee:ff", "AA-BB-CC-DD-EE-FF" or "aabbccddeeff"; null for anything else.</summary>
    public static byte[]? ParseMac(string text)
    {
        var hex = text.Trim().Replace(":", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        if (hex.Length != 12 || !hex.All(Uri.IsHexDigit))
        {
            return null;
        }

        return Convert.FromHexString(hex);
    }

    /// <summary>
    /// Where to send the packet: the limited broadcast, which leaves by this machine's main network; the
    /// broadcast address of each of its own networks; and that of each network the machine to wake was last
    /// seen on (<c>192.168.1.20/24</c>; a bare address takes the prefix of a local network that holds it, else
    /// /24). Routers rarely pass broadcasts on, so it is the agents beside the machine that wake it.
    /// </summary>
    public static List<IPAddress> Destinations(IReadOnlyList<string> targetAddresses, IReadOnlyList<(IPAddress Address, int PrefixLength)> local)
    {
        var destinations = new List<IPAddress> { IPAddress.Broadcast };
        foreach (var (address, prefix) in local)
        {
            Add(Broadcast(address, prefix));
        }

        foreach (var text in targetAddresses)
        {
            var slash = text.IndexOf('/');
            if (!IPAddress.TryParse(slash < 0 ? text : text[..slash], out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            {
                continue;
            }

            int? prefix = slash >= 0 && int.TryParse(text[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var given) && given is >= 8 and <= 32
                ? given
                : local.Where(network => Contains(network.Address, network.PrefixLength, address)).Select(network => (int?)network.PrefixLength).FirstOrDefault();
            Add(Broadcast(address, prefix ?? 24));
        }

        return destinations;

        void Add(IPAddress? destination)
        {
            if (destination is not null && !destinations.Contains(destination))
            {
                destinations.Add(destination);
            }
        }
    }

    /// <summary>The network's broadcast address; null for /31 and /32, which have none.</summary>
    public static IPAddress? Broadcast(IPAddress address, int prefixLength)
    {
        if (prefixLength is < 0 or > 30 || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return null;
        }

        return FromUInt32(ToUInt32(address) | (uint.MaxValue >> prefixLength));
    }

    public static bool Contains(IPAddress network, int prefixLength, IPAddress address)
    {
        if (prefixLength is < 0 or > 32 || network.AddressFamily != AddressFamily.InterNetwork || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var mask = prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);
        return (ToUInt32(network) & mask) == (ToUInt32(address) & mask);
    }

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
    }

    private static IPAddress FromUInt32(uint value) =>
        new([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
}
