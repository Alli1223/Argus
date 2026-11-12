using System.Globalization;
using System.Text.Json;
using Argus.Contracts.Agent;

namespace Argus.Agent.Collection.Unifi;

/// <summary>What the console's health report says about the site as a whole.</summary>
internal sealed record UnifiHealth(double? WanRx, double? WanTx, int? Wired, int? Wireless, int? Guests);

/// <summary>A client's byte counters as the console reports them, from the client's side.</summary>
internal sealed record UnifiClientCounters(
    string Mac, string Name, string? Ip, bool Wired, string? Network, long Downloaded, long Uploaded);

/// <summary>
/// Reads the console's JSON. The older API wraps lists in <c>{"meta": …, "data": [...]}</c>; numbers come as
/// numbers or as strings ("system-stats"), and fields a device does not have are simply missing.
/// </summary>
internal static class UnifiParsers
{
    /// <summary>The list in an answer: its <c>data</c>, or the answer itself when that is the list.</summary>
    public static IEnumerable<JsonElement> Items(JsonElement answer)
    {
        var list = answer.ValueKind == JsonValueKind.Object && answer.TryGetProperty("data", out var data) ? data : answer;
        return list.ValueKind == JsonValueKind.Array ? list.EnumerateArray() : [];
    }

    /// <summary>
    /// Internet traffic from the "wan" subsystem (rx is what comes in from the internet), and clients from
    /// "lan" (wired) and "wlan" (wireless). Guests are counted separately by each and added up here.
    /// </summary>
    public static UnifiHealth ParseHealth(JsonElement answer)
    {
        var subsystems = Items(answer)
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .GroupBy(item => Text(item, "subsystem") ?? "")
            .ToDictionary(group => group.Key, group => group.First());

        var wan = subsystems.GetValueOrDefault("wan");
        var lan = subsystems.GetValueOrDefault("lan");
        var wlan = subsystems.GetValueOrDefault("wlan");
        int? Count(JsonElement subsystem, string name) =>
            subsystem.ValueKind == JsonValueKind.Object && Number(subsystem, name) is { } value ? (int)value : null;

        int? wiredGuests = Count(lan, "num_guest");
        int? wirelessGuests = Count(wlan, "num_guest");
        return new UnifiHealth(
            wan.ValueKind == JsonValueKind.Object ? Number(wan, "rx_bytes-r") : null,
            wan.ValueKind == JsonValueKind.Object ? Number(wan, "tx_bytes-r") : null,
            Sum(Count(lan, "num_user"), wiredGuests),
            Sum(Count(wlan, "num_user"), wirelessGuests),
            Sum(wiredGuests, wirelessGuests));
    }

    public static List<UnifiDevice> ParseDevices(JsonElement answer) => Items(answer)
        .Where(item => item.ValueKind == JsonValueKind.Object && Text(item, "mac") is not null)
        .Select(item =>
        {
            var stats = item.TryGetProperty("system-stats", out var system) && system.ValueKind == JsonValueKind.Object ? system : default;
            // Newer consoles only give a device's rates on its uplink port.
            var uplink = item.TryGetProperty("uplink", out var port) && port.ValueKind == JsonValueKind.Object ? port : default;
            var mac = Text(item, "mac")!;
            return new UnifiDevice
            {
                Mac = mac,
                Name = Text(item, "name") ?? Text(item, "model") ?? mac,
                Model = Text(item, "model"),
                Type = Text(item, "type"),
                // 1 is connected; 0 disconnected; the rest are adopting, upgrading, provisioning and so on.
                Online = Number(item, "state") == 1,
                UptimeSeconds = Number(item, "uptime") is { } uptime ? (long)uptime : null,
                CpuPercent = stats.ValueKind == JsonValueKind.Object ? Number(stats, "cpu") : null,
                MemoryPercent = stats.ValueKind == JsonValueKind.Object ? Number(stats, "mem") : null,
                RxBytesPerSec = Number(item, "rx_bytes-r") ?? (uplink.ValueKind == JsonValueKind.Object ? Number(uplink, "rx_bytes-r") : null),
                TxBytesPerSec = Number(item, "tx_bytes-r") ?? (uplink.ValueKind == JsonValueKind.Object ? Number(uplink, "tx_bytes-r") : null),
                Clients = Number(item, "num_sta") is { } clients ? (int)clients : null,
            };
        })
        .OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>
    /// Each connected client's byte counters. The console counts from its own side, so what it sent
    /// (<c>tx_bytes</c>) is what the client downloaded. Wired clients may only have the <c>wired-</c> counters.
    /// </summary>
    public static List<UnifiClientCounters> ParseClients(JsonElement answer) => Items(answer)
        .Where(item => item.ValueKind == JsonValueKind.Object && Text(item, "mac") is not null)
        .Select(item =>
        {
            var mac = Text(item, "mac")!;
            var wired = item.TryGetProperty("is_wired", out var isWired) && isWired.ValueKind == JsonValueKind.True;
            return new UnifiClientCounters(
                mac,
                Text(item, "name") ?? Text(item, "hostname") ?? mac,
                Text(item, "ip"),
                wired,
                wired ? null : Text(item, "essid"),
                (long)(Number(item, "tx_bytes") ?? Number(item, "wired-tx_bytes") ?? 0),
                (long)(Number(item, "rx_bytes") ?? Number(item, "wired-rx_bytes") ?? 0));
        })
        .ToList();

    /// <summary>The site's display name, from the integration API's list of sites.</summary>
    public static string? SiteName(JsonElement answer, string site) => Items(answer)
        .Where(item => item.ValueKind == JsonValueKind.Object)
        .Where(item => string.Equals(Text(item, "internalReference"), site, StringComparison.OrdinalIgnoreCase))
        .Select(item => Text(item, "name"))
        .FirstOrDefault();

    public static string? Version(JsonElement answer) =>
        answer.ValueKind == JsonValueKind.Object ? Text(answer, "applicationVersion") : null;

    private static int? Sum(int? a, int? b) => a is null && b is null ? null : (a ?? 0) + (b ?? 0);

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text.Trim()
            : null;

    private static double? Number(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }
}
