using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Argus.Server.Features.Power;

/// <summary>Hardware addresses and IPv4 networks for Wake-on-LAN, kept free of I/O so they are easy to test.</summary>
public static class PowerAddresses
{
    /// <summary>"aa:bb:cc:dd:ee:ff" from that, "AA-BB-CC-DD-EE-FF" or "aabbccddeeff"; null for anything else or all zeros.</summary>
    public static string? NormalizeMac(string? text)
    {
        var hex = (text ?? "").Trim().Replace(":", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        if (hex.Length != 12 || !hex.All(Uri.IsHexDigit) || hex.All(c => c == '0'))
        {
            return null;
        }

        hex = hex.ToLowerInvariant();
        return string.Join(':', Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
    }

    /// <summary>An IPv4 address, with or without a prefix length ("192.168.1.255", "192.168.1.20/24").</summary>
    public static bool TryParseNetwork(string? text, out IPAddress address, out int? prefixLength)
    {
        address = IPAddress.None;
        prefixLength = null;
        var value = (text ?? "").Trim();
        var slash = value.IndexOf('/');
        if (!IPAddress.TryParse(slash < 0 ? value : value[..slash], out var parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        if (slash >= 0)
        {
            if (!int.TryParse(value[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var prefix) || prefix > 32)
            {
                return false;
            }

            prefixLength = prefix;
        }

        address = parsed;
        return true;
    }

    /// <summary>
    /// Whether two machines share an IPv4 network, judged from their addresses with prefix lengths: the
    /// relay's prefix decides, as it is the relay's broadcast that has to reach the other machine.
    /// </summary>
    public static bool ShareNetwork(IEnumerable<string> relayNetworks, IEnumerable<string> targetNetworks)
    {
        var targets = targetNetworks
            .Select(text => TryParseNetwork(text, out var address, out _) ? address : null)
            .OfType<IPAddress>()
            .ToList();
        foreach (var text in relayNetworks)
        {
            if (TryParseNetwork(text, out var relay, out var prefix) && prefix is { } length && targets.Any(target => SameNetwork(relay, target, length)))
            {
                return true;
            }
        }

        return false;
    }

    public static bool SameNetwork(IPAddress a, IPAddress b, int prefixLength)
    {
        var mask = prefixLength <= 0 ? 0u : uint.MaxValue << (32 - Math.Min(prefixLength, 32));
        return (ToUInt32(a) & mask) == (ToUInt32(b) & mask);
    }

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
    }
}
