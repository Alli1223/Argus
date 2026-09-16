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
        Guid id, ClaimsPrincipal user, ArgusDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var alert = await db.Alerts.VisibleTo(user).SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (alert is null)
        {
            return TypedResults.NotFound();
        }

        if (alert.Status != AlertStatus.Firing)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Alert already resolved",
                detail: "Only firing alerts can be acknowledged.");
        }

        if (alert.AcknowledgedAt is null)
        {
            alert.AcknowledgedAt = time.GetUtcNow();
            alert.AcknowledgedById = user.GetUserId();
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(await Project(db, db.Alerts.Where(a => a.Id == id)).SingleAsync(cancellationToken));
    }

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
            db.Users.Where(u => u.Id == alert.AcknowledgedById).Select(u => u.DisplayName).FirstOrDefault()));
}
