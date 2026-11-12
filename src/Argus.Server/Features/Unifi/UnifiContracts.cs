namespace Argus.Server.Features.Unifi;

/// <summary>
/// A UniFi network one of the user's hosts reads: when the agent last checked its console, what it found
/// wrong if anything, the newest internet traffic and client counts, and the console's devices.
/// </summary>
public sealed record UnifiNetworkResponse(
    Guid HostId,
    string HostName,
    DateTimeOffset CheckedAt,
    string? Site,
    string? Version,
    string? Problem,
    UnifiNow? Now,
    IReadOnlyList<UnifiDeviceResponse> Devices);

/// <summary>The newest reading of the network as a whole.</summary>
public sealed record UnifiNow(
    DateTimeOffset Time,
    double? WanRxBytesPerSec,
    double? WanTxBytesPerSec,
    int? WiredClients,
    int? WirelessClients,
    int? GuestClients);

/// <param name="StateSince">When the device last went online or offline.</param>
public sealed record UnifiDeviceResponse(
    string Mac,
    string Name,
    string? Model,
    string? Type,
    bool Online,
    long? UptimeSeconds,
    double? CpuPercent,
    double? MemoryPercent,
    double? RxBytesPerSec,
    double? TxBytesPerSec,
    int? Clients,
    DateTimeOffset SeenAt,
    DateTimeOffset StateSince);

/// <summary>What one client moved over a range, and what it was last called.</summary>
public sealed record UnifiClientTotal(
    string Mac,
    string Name,
    string? Ip,
    bool Wired,
    string? Network,
    long DownloadedBytes,
    long UploadedBytes,
    DateTimeOffset LastSeen);
