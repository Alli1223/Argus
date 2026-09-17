using System.Security.Claims;
using Argus.Server.Data;
using Argus.Server.Features.Agents;
using Argus.Server.Features.Hosts;
using Argus.Server.Features.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Argus.Server.Features.Containers;

/// <summary>Hosts' Docker containers. People see the containers of the hosts they can see.</summary>
public static class ContainerEndpoints
{
    public static IEndpointRouteBuilder MapContainerEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/containers", GetAllAsync).WithTags("Containers");

        var host = routes.MapGroup("/hosts/{id:guid}/containers").WithTags("Containers");
        host.MapGet("/", GetHostAsync);
        host.MapGet("/{name}", GetContainerAsync);
        host.MapGet("/{name}/metrics", GetHistoryAsync);

        return routes;
    }

    /// <summary>Every visible host that reports containers, by name, with its containers.</summary>
    private static async Task<List<ContainerHost>> GetAllAsync(
        ClaimsPrincipal user, ArgusDbContext db, ContainerStore store, CancellationToken cancellationToken)
    {
        var hosts = await db.Hosts.AsNoTracking().VisibleTo(user)
            .OrderBy(host => host.DisplayName)
            .Select(host => new { host.Id, host.DisplayName })
            .ToListAsync(cancellationToken);
        var reports = (await store.GetHostsAsync(hosts.Select(host => host.Id).ToList(), cancellationToken))
            .ToDictionary(report => report.HostId);

        return hosts
            .Where(host => reports.ContainsKey(host.Id))
            .Select(host =>
            {
                var report = reports[host.Id];
                return new ContainerHost(host.Id, host.DisplayName, report.CheckedAt, report.Problem, report.ActionsEnabled, report.Containers);
            })
            .ToList();
    }

    private static async Task<Results<Ok<HostContainers>, NotFound>> GetHostAsync(
        Guid id, ClaimsPrincipal user, ArgusDbContext db, ContainerStore store, CancellationToken cancellationToken)
    {
        if (!await db.Hosts.VisibleTo(user).AnyAsync(host => host.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await store.GetHostAsync(id, cancellationToken));
    }

    private static async Task<Results<Ok<ContainerDetail>, NotFound>> GetContainerAsync(
        Guid id, string name, ClaimsPrincipal user, ArgusDbContext db, ContainerStore store, CancellationToken cancellationToken)
    {
        var host = await db.Hosts.AsNoTracking().VisibleTo(user)
            .Where(host => host.Id == id)
            .Select(host => new { host.Id, host.DisplayName })
            .SingleOrDefaultAsync(cancellationToken);
        if (host is null || await store.GetContainerAsync(id, name, cancellationToken) is not { } found)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new ContainerDetail(host.Id, host.DisplayName, found.ActionsEnabled, found.Container, found.Events));
    }

    private static async Task<Results<Ok<MetricSeries>, NotFound, ValidationProblem>> GetHistoryAsync(
        Guid id,
        string name,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? points,
        ClaimsPrincipal user,
        ArgusDbContext db,
        ContainerStore store,
        IOptions<AgentOptions> agents,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!await db.Hosts.VisibleTo(user).AnyAsync(host => host.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        if (SeriesRange.TryCreate(from, to, points, time.GetUtcNow(), out var range) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return TypedResults.Ok(await store.GetHistoryAsync(
            id, name, range, TimeSpan.FromSeconds(agents.Value.CollectionIntervalSeconds), cancellationToken));
    }
}
