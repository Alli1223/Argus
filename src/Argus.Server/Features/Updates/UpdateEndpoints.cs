using System.Security.Claims;
using Argus.Contracts.Agent;
using Argus.Server.Data;
using Argus.Server.Features.Agents;
using Argus.Server.Features.Auth;
using Argus.Server.Features.Hosts;
using Argus.Server.Features.Metrics;
using Argus.Server.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Argus.Server.Features.Updates;

public sealed record ReleaseSummary(string Version, string Tag, string Name, string Notes, Uri Url, DateTimeOffset PublishedAt);

/// <summary>This server's version against the latest release, for administrators.</summary>
public sealed record ServerUpdateInfo(
    string CurrentVersion,
    bool Enabled,
    ReleaseSummary? Latest,
    bool UpdateAvailable,
    DateTimeOffset? CheckedAt,
    string? Error);

public sealed record AgentUpdateRequests(int Requested);

public static class UpdateEndpoints
{
    /// <summary>Update notices for administrators, and asking agents to update. Mapped under /api.</summary>
    public static IEndpointRouteBuilder MapUpdateEndpoints(this IEndpointRouteBuilder routes)
    {
        var updates = routes.MapGroup("/updates")
            .WithTags("Updates")
            .RequireAuthorization(policy => policy.RequireRole(Roles.Admin));
        updates.MapGet("/", (UpdateStatus status, IOptions<UpdateOptions> options) => Describe(status.Current, options.Value));
        updates.MapPost("/check", CheckAsync);
        updates.MapGet("/server", (ServerUpdates server) => server.Describe());
        updates.MapPost("/server", RequestServerUpdate);

        var hosts = routes.MapGroup("/hosts").WithTags("Hosts");
        hosts.MapPost("/agent-updates", RequestAllAsync);
        hosts.MapPost("/{id:guid}/agent-update", RequestAsync);
        hosts.MapDelete("/{id:guid}/agent-update", CancelAsync);

        return routes;
    }

    /// <summary>What agents use to update themselves.</summary>
    public static IEndpointRouteBuilder MapAgentUpdateEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet(AgentApi.UpdateOffer, OfferAsync)
            .WithTags("Agent")
            .RequireAuthorization(AgentKeyDefaults.Policy);

        routes.MapGet(AgentApi.UpdateDownload, DownloadAsync)
            .WithTags("Agent")
            .RequireAuthorization(AgentKeyDefaults.Policy)
            .RequireRateLimiting(RateLimiting.AgentIngestPolicy);

        routes.MapPost(AgentApi.UpdateResult, ReportResultAsync)
            .WithTags("Agent")
            .RequireAuthorization(AgentKeyDefaults.Policy);

        return routes;
    }

    private static ServerUpdateInfo Describe(UpdateStatus.Snapshot snapshot, UpdateOptions options)
    {
        var latest = snapshot.Latest;
        return new ServerUpdateInfo(
            ServerVersion.Current,
            options.CheckForUpdates,
            latest is null ? null : new ReleaseSummary(latest.Version, latest.Tag, latest.Name, latest.Notes, latest.Url, latest.PublishedAt),
            latest is not null && ReleaseVersions.IsNewer(latest.Version, ServerVersion.Current),
            snapshot.CheckedAt,
            snapshot.Error);
    }

    private static async Task<Results<Ok<ServerUpdateInfo>, ProblemHttpResult>> CheckAsync(
        UpdateChecker checker, IOptions<UpdateOptions> options, CancellationToken cancellationToken)
    {
        if (!options.Value.CheckForUpdates)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Update checks are switched off",
                detail: "Set Argus:Updates:CheckForUpdates to true to look for new releases.");
        }

        return TypedResults.Ok(Describe(await checker.CheckAsync(cancellationToken), options.Value));
    }

    /// <summary>Asks the updater to install the latest release; it starts within a few seconds.</summary>
    private static Results<Accepted<ServerSelfUpdate>, ProblemHttpResult> RequestServerUpdate(
        ServerUpdateRequest request, ClaimsPrincipal user, ServerUpdates server)
    {
        if (server.Request(request.Version.Trim(), user.Identity?.Name) is { } problem)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Argus cannot update now", detail: problem);
        }

        return TypedResults.Accepted("/api/updates/server", server.Describe());
    }

    private static async Task<Results<Ok<HostDetail>, NotFound, ProblemHttpResult>> RequestAsync(
        Guid id,
        ClaimsPrincipal user,
        ArgusDbContext db,
        AgentUpdates updates,
        UpdateStatus status,
        TimeSeriesQueries series,
        IOptions<AgentOptions> agents,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var host = await db.Hosts.VisibleTo(user).SingleOrDefaultAsync(h => h.Id == id, cancellationToken);
        if (host is null)
        {
            return TypedResults.NotFound();
        }

        if (updates.Request(host) is { } problem)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "No agent update", detail: problem);
        }

        await db.SaveChangesAsync(cancellationToken);
        var latest = await series.GetLatestAsync([host.Id], cancellationToken);
        return TypedResults.Ok(host.ToDetail(latest.GetValueOrDefault(host.Id), agents.Value, time.GetUtcNow(), status.Current.Latest));
    }

    /// <summary>Asks every host the user can see whose agent could update, and has not already been asked.</summary>
    private static async Task<AgentUpdateRequests> RequestAllAsync(
        ClaimsPrincipal user, ArgusDbContext db, AgentUpdates updates, UpdateStatus status, CancellationToken cancellationToken)
    {
        var release = status.Current.Latest;
        var hosts = await db.Hosts.VisibleTo(user).ToListAsync(cancellationToken);
        var requested = 0;
        foreach (var host in hosts.Where(host => AgentUpdates.AvailableFor(host, release) is { } version && host.AgentUpdateVersion != version))
        {
            if (updates.Request(host) is null)
            {
                requested++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new AgentUpdateRequests(requested);
    }

    private static async Task<Results<Ok<HostDetail>, NotFound>> CancelAsync(
        Guid id,
        ClaimsPrincipal user,
        ArgusDbContext db,
        UpdateStatus status,
        TimeSeriesQueries series,
        IOptions<AgentOptions> agents,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var host = await db.Hosts.VisibleTo(user).SingleOrDefaultAsync(h => h.Id == id, cancellationToken);
        if (host is null)
        {
            return TypedResults.NotFound();
        }

        AgentUpdates.Cancel(host);
        await db.SaveChangesAsync(cancellationToken);
        var latest = await series.GetLatestAsync([host.Id], cancellationToken);
        return TypedResults.Ok(host.ToDetail(latest.GetValueOrDefault(host.Id), agents.Value, time.GetUtcNow(), status.Current.Latest));
    }

    private static async Task<Results<Ok<AgentUpdateOffer>, NoContent>> OfferAsync(
        ClaimsPrincipal principal, AgentUpdates updates, CancellationToken cancellationToken) =>
        await updates.OfferAsync(principal.GetHostId(), cancellationToken) is { } offer
            ? TypedResults.Ok(offer)
            : TypedResults.NoContent();

    private static async Task<Results<PhysicalFileHttpResult, NotFound>> DownloadAsync(
        ClaimsPrincipal principal, AgentUpdates updates, CancellationToken cancellationToken) =>
        await updates.PackageForAsync(principal.GetHostId(), cancellationToken) is { } package
            ? TypedResults.PhysicalFile(package.FilePath, "application/octet-stream")
            : TypedResults.NotFound();

    private static async Task<NoContent> ReportResultAsync(
        AgentUpdateResult result,
        ClaimsPrincipal principal,
        AgentUpdates updates,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        var hostId = principal.GetHostId();
        if (!result.Succeeded)
        {
            var error = string.IsNullOrWhiteSpace(result.Error) ? "The agent could not install the update." : result.Error.Trim();
            loggers.CreateLogger(typeof(UpdateEndpoints).FullName!)
                .LogWarning("Host {HostId} could not update its agent to {Version}: {Error}", hostId, result.Version, error);
            await updates.FailAsync(hostId, error, cancellationToken);
        }

        return TypedResults.NoContent();
    }
}
