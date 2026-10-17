using System.Security.Claims;
using Argus.Server.Data;
using Argus.Server.Features.Auth;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Argus.Server.Features.Alerts;

/// <summary>Alerts on the user's hosts (administrators see every alert).</summary>
public static class AlertEndpoints
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    public static IEndpointRouteBuilder MapAlertEndpoints(this IEndpointRouteBuilder routes)
    {
        var alerts = routes.MapGroup("/alerts").WithTags("Alerts");

        alerts.MapGet("/", ListAsync);
        alerts.MapGet("/summary", GetSummaryAsync);
        alerts.MapPost("/{id:guid}/acknowledge", AcknowledgeAsync);
        alerts.MapPost("/{id:guid}/ignore", IgnoreAsync);

        var ignores = routes.MapGroup("/alert-ignores").WithTags("Alerts");
        ignores.MapGet("/", ListIgnoresAsync);
        ignores.MapDelete("/{id:guid}", ResumeAsync);

        return routes;
    }

    internal static IQueryable<Alert> VisibleTo(this IQueryable<Alert> alerts, ClaimsPrincipal user)
    {
        if (user.IsAdmin())
        {
            return alerts;
        }

        var userId = user.GetUserId();
        return alerts.Where(alert => alert.OwnerId == userId);
    }

    internal static async Task<AlertCounts> CountFiringAsync(IQueryable<Alert> alerts, CancellationToken cancellationToken)
    {
        var counts = await alerts
            .Where(alert => alert.Status == AlertStatus.Firing)
            .GroupBy(alert => alert.Severity)
            .Select(group => new { Severity = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        int Of(AlertSeverity severity) => counts.FirstOrDefault(c => c.Severity == severity)?.Count ?? 0;
        return new AlertCounts(Of(AlertSeverity.Critical), Of(AlertSeverity.Warning), Of(AlertSeverity.Info));
    }

    private static async Task<Results<Ok<AlertPage>, ValidationProblem>> ListAsync(
        AlertStatus? status,
        Guid? hostId,
        AlertSeverity? severity,
        int? page,
        int? pageSize,
        ClaimsPrincipal user,
        ArgusDbContext db,
        CancellationToken cancellationToken)
    {
        var number = page ?? 1;
        var size = pageSize ?? DefaultPageSize;
        if (number < 1 || size is < 1 or > MaxPageSize)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["pageSize"] = [$"'page' starts at 1 and 'pageSize' must be between 1 and {MaxPageSize}."],
            });
        }

        var query = db.Alerts.AsNoTracking().VisibleTo(user);
        if (status is { } statusFilter)
        {
            query = query.Where(alert => alert.Status == statusFilter);
        }

        if (hostId is { } hostFilter)
        {
            query = query.Where(alert => alert.HostId == hostFilter);
        }

        if (severity is { } severityFilter)
        {
            query = query.Where(alert => alert.Severity == severityFilter);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await Project(db, query.OrderByDescending(alert => alert.FiredAt).Skip((number - 1) * size).Take(size))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new AlertPage(items, total, number, size));
    }

    private static Task<AlertCounts> GetSummaryAsync(ClaimsPrincipal user, ArgusDbContext db, CancellationToken cancellationToken) =>
        CountFiringAsync(db.Alerts.VisibleTo(user), cancellationToken);

    private static async Task<Results<Ok<AlertResponse>, NotFound, ProblemHttpResult>> AcknowledgeAsync(
        Guid id, ClaimsPrincipal user, ArgusDbContext db, AlertActions actions, CancellationToken cancellationToken)
    {
        var alert = await db.Alerts.VisibleTo(user).SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (alert is null)
        {
            return TypedResults.NotFound();
        }

        if (await actions.AcknowledgeAsync(alert, user.GetUserId(), cancellationToken) == AlertActionOutcome.NotFiring)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Alert already resolved",
                detail: "Only firing alerts can be acknowledged.");
        }

        return TypedResults.Ok(await Project(db, db.Alerts.Where(a => a.Id == id)).SingleAsync(cancellationToken));
    }

    /// <summary>Stops the alert's rule watching this host and resource, closing the alert if it is firing.</summary>
    private static async Task<Results<Ok<AlertIgnoreResponse>, NotFound, ProblemHttpResult>> IgnoreAsync(
        Guid id, ClaimsPrincipal user, ArgusDbContext db, AlertActions actions, CancellationToken cancellationToken)
    {
        var alert = await db.Alerts.VisibleTo(user).SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (alert is null)
        {
            return TypedResults.NotFound();
        }

        var (outcome, ignore) = await actions.IgnoreAsync(alert, user.GetUserId(), cancellationToken);
        if (outcome == AlertActionOutcome.NoRule)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The rule is gone",
                detail: "This alert's rule was deleted, so nothing is watching it any more.");
        }

        return TypedResults.Ok(await ProjectIgnores(db, db.AlertIgnores.Where(i => i.Id == ignore!.Id)).SingleAsync(cancellationToken));
    }

    private static Task<List<AlertIgnoreResponse>> ListIgnoresAsync(ClaimsPrincipal user, ArgusDbContext db, CancellationToken cancellationToken) =>
        ProjectIgnores(db, IgnoresVisibleTo(db, user).OrderByDescending(i => i.CreatedAt)).ToListAsync(cancellationToken);

    /// <summary>Starts watching the host and resource again. A problem that is still there fires again at the next check.</summary>
    private static async Task<Results<NoContent, NotFound>> ResumeAsync(
        Guid id, ClaimsPrincipal user, ArgusDbContext db, CancellationToken cancellationToken)
    {
        var deleted = await IgnoresVisibleTo(db, user).Where(i => i.Id == id).ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static IQueryable<AlertIgnore> IgnoresVisibleTo(ArgusDbContext db, ClaimsPrincipal user)
    {
        if (user.IsAdmin())
        {
            return db.AlertIgnores;
        }

        var userId = user.GetUserId();
        return db.AlertIgnores.Where(ignore => ignore.OwnerId == userId);
    }

    private static IQueryable<AlertIgnoreResponse> ProjectIgnores(ArgusDbContext db, IQueryable<AlertIgnore> ignores) =>
        ignores.Select(ignore => new AlertIgnoreResponse(
            ignore.Id,
            ignore.RuleId,
            ignore.Rule!.Name,
            ignore.HostId,
            ignore.Host!.DisplayName,
            ignore.ResourceKey,
            ignore.Title,
            ignore.CreatedAt,
            db.Users.Where(u => u.Id == ignore.CreatedById).Select(u => u.DisplayName).FirstOrDefault()));

    private static IQueryable<AlertResponse> Project(ArgusDbContext db, IQueryable<Alert> alerts) =>
        alerts.Select(alert => new AlertResponse(
            alert.Id,
            alert.RuleId,
            alert.Rule != null ? alert.Rule.Name : null,
            alert.HostId,
            alert.Host!.DisplayName,
            alert.ResourceKey,
            alert.Title,
            alert.Metric,
            alert.Condition,
            alert.Operator,
            alert.Threshold,
            alert.Severity,
            alert.Status,
            alert.Value,
            alert.Baseline,
            alert.FiredAt,
            alert.ResolvedAt,
            alert.AcknowledgedAt,
            db.Users.Where(u => u.Id == alert.AcknowledgedById).Select(u => u.DisplayName).FirstOrDefault(),
            alert.IgnoredAt));
}
