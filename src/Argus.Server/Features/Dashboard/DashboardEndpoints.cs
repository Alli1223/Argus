using System.Security.Claims;
using Argus.Server.Data;
using Argus.Server.Features.Agents;
using Argus.Server.Features.Alerts;
using Argus.Server.Features.Hosts;
using Argus.Server.Features.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Argus.Server.Features.Dashboard;

public sealed record DashboardSummary(
    int TotalHosts,
    int OnlineHosts,
    int OfflineHosts,
    IReadOnlyDictionary<string, int> Platforms,
    AlertCounts ActiveAlerts,
    IReadOnlyList<HostSummary> BusiestByCpu,
    IReadOnlyList<HostSummary> FullestDisks);

public static class DashboardEndpoints
{
    private const int TopCount = 5;

    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/dashboard/summary", GetSummaryAsync).WithTags("Dashboard");
        return routes;
    }

    private static async Task<DashboardSummary> GetSummaryAsync(
        ClaimsPrincipal user,
        ArgusDbContext db,
        TimeSeriesQueries series,
        IOptions<AgentOptions> agents,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var hosts = await db.Hosts.AsNoTracking().VisibleTo(user).ToListAsync(cancellationToken);
        var latest = await series.GetLatestAsync(hosts.Select(host => host.Id).ToList(), cancellationToken);
        var now = time.GetUtcNow();
        var summaries = hosts.Select(host => host.ToSummary(latest.GetValueOrDefault(host.Id), agents.Value, now)).ToList();
        var online = summaries.Where(host => host.Status == HostStatus.Online).ToList();

        return new DashboardSummary(
            TotalHosts: summaries.Count,
            OnlineHosts: online.Count,
            OfflineHosts: summaries.Count - online.Count,
            Platforms: summaries.GroupBy(host => host.Platform.ToString()).ToDictionary(group => group.Key, group => group.Count()),
            ActiveAlerts: await AlertEndpoints.CountFiringAsync(db.Alerts.VisibleTo(user), cancellationToken),
            BusiestByCpu: online
                .Where(host => host.Latest is not null)
                .OrderByDescending(host => host.Latest!.CpuPercent)
                .Take(TopCount)
                .ToList(),
            FullestDisks: summaries
                .Where(host => host.Latest?.DiskUsedPercent is not null)
                .OrderByDescending(host => host.Latest!.DiskUsedPercent)
                .Take(TopCount)
                .ToList());
    }
}
