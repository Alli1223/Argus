using System.Security.Claims;
using Argus.Server.Features.Agents;
using Argus.Server.Features.Auth;
using Argus.Server.Features.Updates;

namespace Argus.Server.Features.Hosts;

internal static class HostAccess
{
    /// <summary>Hosts the user may see: their own, or every host for administrators.</summary>
    public static IQueryable<MonitoredHost> VisibleTo(this IQueryable<MonitoredHost> hosts, ClaimsPrincipal user)
    {
        if (user.IsAdmin())
        {
            return hosts;
        }

        var userId = user.GetUserId();
        return hosts.Where(host => host.OwnerId == userId);
    }

    public static HostStatus StatusAt(this MonitoredHost host, DateTimeOffset now, AgentOptions options) =>
        StatusOf(host.LastSeenAt, now, options);

    public static HostStatus StatusOf(DateTimeOffset? lastSeen, DateTimeOffset now, AgentOptions options) =>
        lastSeen is { } seen && now - seen <= TimeSpan.FromSeconds(options.OfflineAfterSeconds)
            ? HostStatus.Online
            : HostStatus.Offline;

    public static HostSummary ToSummary(
        this MonitoredHost host, LatestMetrics? latest, AgentOptions options, DateTimeOffset now, ReleaseInfo? release) =>
        new(host.Id, host.DisplayName, host.Hostname, host.Platform, host.OsName, host.Tags, host.StatusAt(now, options),
            host.LastSeenAt, host.AgentVersion, host.OwnerId, latest, host.AgentUpdateState(release));

    public static HostDetail ToDetail(
        this MonitoredHost host, LatestMetrics? latest, AgentOptions options, DateTimeOffset now, ReleaseInfo? release) =>
        new(host.Id, host.DisplayName, host.Hostname, host.Platform, host.OsName, host.OsVersion, host.KernelVersion,
            host.Architecture, host.CpuModel, host.CpuCores, host.CpuLogicalProcessors, host.MemoryTotalBytes, host.BootTime,
            host.IpAddresses, host.AgentVersion, host.Tags, host.Notes, host.StatusAt(now, options), host.CreatedAt,
            host.LastSeenAt, host.InventoryUpdatedAt, host.OwnerId, latest, host.AgentUpdateState(release));

    private static HostAgentUpdate? AgentUpdateState(this MonitoredHost host, ReleaseInfo? release)
    {
        var available = AgentUpdates.AvailableFor(host, release);
        return available is null && host.AgentUpdateVersion is null && host.AgentUpdateError is null
            ? null
            : new HostAgentUpdate(available, host.AgentUpdateVersion, host.AgentUpdateRequestedAt, host.AgentUpdateError);
    }
}
