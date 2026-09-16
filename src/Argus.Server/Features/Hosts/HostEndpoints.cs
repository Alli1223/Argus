using System.Security.Claims;
using Argus.Server.Data;
using Argus.Server.Features.Agents;
using Argus.Server.Features.Metrics;
using Argus.Server.Features.Updates;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Argus.Server.Features.Hosts;

/// <summary>Hosts and their metrics. Users see their own hosts; administrators see all of them.</summary>
public static class HostEndpoints
{
    public static IEndpointRouteBuilder MapHostEndpoints(this IEndpointRouteBuilder routes)
    {
        var hosts = routes.MapGroup("/hosts").WithTags("Hosts");

        hosts.MapGet("/", ListAsync);
        hosts.MapGet("/temperatures", GetFleetTemperaturesAsync);
        hosts.MapGet("/{id:guid}", GetAsync);
        hosts.MapPatch("/{id:guid}", UpdateAsync);
        hosts.MapDelete("/{id:guid}", DeleteAsync);
        hosts.MapGet("/{id:guid}/metrics", GetMetricsAsync);
        hosts.MapGet("/{id:guid}/filesystems", GetFilesystemsAsync);
        hosts.MapGet("/{id:guid}/filesystems/history", GetFilesystemHistoryAsync);
        hosts.MapGet("/{id:guid}/network", GetNetworkHistoryAsync);
        hosts.MapGet("/{id:guid}/temperatures", GetTemperatureHistoryAsync);
        hosts.MapGet("/{id:guid}/processes", GetProcessesAsync);
        hosts.MapGet("/{id:guid}/services", GetServicesAsync);

        return routes;
    }

    private static async Task<List<HostSummary>> ListAsync(
        ClaimsPrincipal user,
        ArgusDbContext db,
        TimeSeriesQueries series,
        IOptions<AgentOptions> agents,
        UpdateStatus updates,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var hosts = await db.Hosts.AsNoTracking().VisibleTo(user).OrderBy(host => host.DisplayName).ToListAsync(cancellationToken);
        var latest = await series.GetLatestAsync(hosts.Select(host => host.Id).ToList(), cancellationToken);
        var now = time.GetUtcNow();
        return hosts.Select(host => host.ToSummary(latest.GetValueOrDefault(host.Id), agents.Value, now, updates.Current.Latest)).ToList();
    }

    private static async Task<Results<Ok<HostDetail>, NotFound>> GetAsync(
        Guid id,
        ClaimsPrincipal user,
        ArgusDbContext db,
        TimeSeriesQueries series,
        IOptions<AgentOptions> agents,
        UpdateStatus updates,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var host = await db.Hosts.AsNoTracking().VisibleTo(user).SingleOrDefaultAsync(h => h.Id == id, cancellationToken);
        if (host is null)
        {
            return TypedResults.NotFound();
        }

        var latest = await series.GetLatestAsync([host.Id], cancellationToken);
        return TypedResults.Ok(host.ToDetail(latest.GetValueOrDefault(host.Id), agents.Value, time.GetUtcNow(), updates.Current.Latest));
    }

    private static async Task<Results<Ok<HostDetail>, NotFound, ValidationProblem>> UpdateAsync(
        Guid id,
        UpdateHostRequest request,
        ClaimsPrincipal user,
        ArgusDbContext db,
        TimeSeriesQueries series,
        IOptions<AgentOptions> agents,
        UpdateStatus updates,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var host = await db.Hosts.VisibleTo(user).SingleOrDefaultAsync(h => h.Id == id, cancellationToken);
        if (host is null)
        {
            return TypedResults.NotFound();
        }

        if (request.DisplayName is { } displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return Invalid("displayName", "The name cannot be empty.");
            }

            host.DisplayName = displayName.Trim();
        }

        if (request.Tags is { } tags)
        {
            if (!HostTags.TryNormalize(tags, out var normalized, out var error))
            {
                return Invalid("tags", error);
            }

            host.Tags = normalized;
        }

        if (request.Notes is { } notes)
        {
            host.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        }

        await db.SaveChangesAsync(cancellationToken);

        var latest = await series.GetLatestAsync([host.Id], cancellationToken);
        return TypedResults.Ok(host.ToDetail(latest.GetValueOrDefault(host.Id), agents.Value, time.GetUtcNow(), updates.Current.Latest));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(
        Guid id,
        ClaimsPrincipal user,
        ArgusDbContext db,
        TimeSeriesQueries series,
        AgentKeyValidator agentKeys,
        CancellationToken cancellationToken)
    {
        var host = await db.Hosts.VisibleTo(user).SingleOrDefaultAsync(h => h.Id == id, cancellationToken);
        if (host is null)
        {
            return TypedResults.NotFound();
        }

        db.Hosts.Remove(host);
        await db.SaveChangesAsync(cancellationToken);

        // Stop accepting the agent's key right away rather than when its cache entry expires.
        agentKeys.Invalidate(host.AgentKeyHash);
        await series.DeleteHostDataAsync(host.Id, cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<MetricSeries>, NotFound, ValidationProblem>> GetMetricsAsync(
        Guid id,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? points,
        ClaimsPrincipal user,
        ArgusDbContext db,
        TimeSeriesQueries series,
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

        return TypedResults.Ok(await series.GetHostSeriesAsync(id, range, CollectionInterval(agents), cancellationToken));
    }

    private static async Task<Results<Ok<List<FilesystemSnapshot>>, NotFound>> GetFilesystemsAsync(
        Guid id, ClaimsPrincipal user, ArgusDbContext db, TimeSeriesQueries series, CancellationToken cancellationToken)
    {
        if (!await db.Hosts.VisibleTo(user).AnyAsync(host => host.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await series.GetFilesystemsAsync(id, cancellationToken));
    }

    private static async Task<Results<Ok<MetricSeries>, NotFound, ValidationProblem>> GetFilesystemHistoryAsync(
        Guid id,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? points,
        ClaimsPrincipal user,
        ArgusDbContext db,
        TimeSeriesQueries series,
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

        return TypedResults.Ok(await series.GetFilesystemHistoryAsync(id, range, CollectionInterval(agents), cancellationToken));
    }

    private static async Task<Results<Ok<MetricSeries>, NotFound, ValidationProblem>> GetNetworkHistoryAsync(
        Guid id,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? points,
        ClaimsPrincipal user,
        ArgusDbContext db,
        TimeSeriesQueries series,
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

        return TypedResults.Ok(await series.GetNetworkHistoryAsync(id, range, CollectionInterval(agents), cancellationToken));
    }

    private static async Task<Results<Ok<MetricSeries>, NotFound, ValidationProblem>> GetTemperatureHistoryAsync(
        Guid id,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? points,
        ClaimsPrincipal user,
        ArgusDbContext db,
        TimeSeriesQueries series,
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

        var histories = await series.GetTemperatureHistoryAsync([id], range, CollectionInterval(agents), cancellationToken);
        return TypedResults.Ok(histories[id]);
    }

    /// <summary>The temperature history of every visible host that reported temperatures in the range, by name.</summary>
    private static async Task<Results<Ok<List<HostTemperatures>>, ValidationProblem>> GetFleetTemperaturesAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? points,
        ClaimsPrincipal user,
        ArgusDbContext db,
        TimeSeriesQueries series,
        IOptions<AgentOptions> agents,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (SeriesRange.TryCreate(from, to, points, time.GetUtcNow(), out var range) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var hosts = await db.Hosts.AsNoTracking().VisibleTo(user)
            .OrderBy(host => host.DisplayName)
            .Select(host => new { host.Id, host.DisplayName })
            .ToListAsync(cancellationToken);
        var histories = await series.GetTemperatureHistoryAsync(
            hosts.Select(host => host.Id).ToList(), range, CollectionInterval(agents), cancellationToken);

        return TypedResults.Ok(hosts
            .Where(host => histories[host.Id].Series.Count > 0)
            .Select(host => new HostTemperatures(host.Id, host.DisplayName, histories[host.Id]))
            .ToList());
    }

    private static async Task<Results<Ok<ProcessSnapshot>, NoContent, NotFound>> GetProcessesAsync(
        Guid id, ClaimsPrincipal user, ArgusDbContext db, TimeSeriesQueries series, CancellationToken cancellationToken)
    {
        if (!await db.Hosts.VisibleTo(user).AnyAsync(host => host.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        return await series.GetProcessesAsync(id, cancellationToken) is { } snapshot
            ? TypedResults.Ok(snapshot)
            : TypedResults.NoContent();
    }

    private static async Task<Results<Ok<ServiceStatus>, NotFound>> GetServicesAsync(
        Guid id, ClaimsPrincipal user, ArgusDbContext db, TimeSeriesQueries series, CancellationToken cancellationToken)
    {
        if (!await db.Hosts.VisibleTo(user).AnyAsync(host => host.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await series.GetServicesAsync(id, cancellationToken));
    }

    private static TimeSpan CollectionInterval(IOptions<AgentOptions> agents) =>
        TimeSpan.FromSeconds(agents.Value.CollectionIntervalSeconds);

    private static ValidationProblem Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
