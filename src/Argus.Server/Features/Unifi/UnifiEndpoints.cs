using System.Security.Claims;
using Argus.Server.Data;
using Argus.Server.Features.Agents;
using Argus.Server.Features.Hosts;
using Argus.Server.Features.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Argus.Server.Features.Unifi;

/// <summary>UniFi networks the user's hosts read from UniFi consoles.</summary>
public static class UnifiEndpoints
{
    public static IEndpointRouteBuilder MapUnifiEndpoints(this IEndpointRouteBuilder routes)
    {
        var unifi = routes.MapGroup("/unifi").WithTags("UniFi");
        unifi.MapGet("/", ListAsync);
        unifi.MapGet("/{hostId:guid}/history", GetHistoryAsync);
        unifi.MapGet("/{hostId:guid}/clients", GetClientsAsync);
        return routes;
    }

    private static async Task<List<UnifiNetworkResponse>> ListAsync(
        ClaimsPrincipal user, ArgusDbContext db, UnifiQueries queries, CancellationToken cancellationToken)
    {
        var hosts = await db.Hosts.AsNoTracking().VisibleTo(user)
            .ToDictionaryAsync(host => host.Id, host => host.DisplayName, cancellationToken);
        return await queries.GetNetworksAsync(hosts, cancellationToken);
    }

    private static async Task<Results<Ok<MetricSeries>, NotFound, ValidationProblem>> GetHistoryAsync(
        Guid hostId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? points,
        ClaimsPrincipal user,
        ArgusDbContext db,
        UnifiQueries queries,
        IOptions<AgentOptions> agents,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!await db.Hosts.VisibleTo(user).AnyAsync(host => host.Id == hostId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        if (SeriesRange.TryCreate(from, to, points, time.GetUtcNow(), out var range) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return TypedResults.Ok(await queries.GetHistoryAsync(
            hostId, range, TimeSpan.FromSeconds(agents.Value.CollectionIntervalSeconds), cancellationToken));
    }

    private static async Task<Results<Ok<List<UnifiClientTotal>>, NotFound, ValidationProblem>> GetClientsAsync(
        Guid hostId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        ClaimsPrincipal user,
        ArgusDbContext db,
        UnifiQueries queries,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!await db.Hosts.VisibleTo(user).AnyAsync(host => host.Id == hostId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        if (SeriesRange.TryCreate(from, to, null, time.GetUtcNow(), out var range) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return TypedResults.Ok(await queries.GetClientTotalsAsync(hostId, range, cancellationToken));
    }
}
