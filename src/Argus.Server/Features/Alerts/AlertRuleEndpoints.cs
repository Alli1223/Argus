using System.Security.Claims;
using Argus.Server.Data;
using Argus.Server.Features.Auth;
using Argus.Server.Features.Hosts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Argus.Server.Features.Alerts;

/// <summary>Alert rules are personal: each user manages the rules that watch their own hosts.</summary>
public static class AlertRuleEndpoints
{
    public static IEndpointRouteBuilder MapAlertRuleEndpoints(this IEndpointRouteBuilder routes)
    {
        var rules = routes.MapGroup("/alert-rules").WithTags("Alert rules");

        rules.MapGet("/", ListAsync);
        rules.MapGet("/{id:guid}", GetAsync);
        rules.MapPost("/", CreateAsync);
        rules.MapPut("/{id:guid}", UpdateAsync);
        rules.MapDelete("/{id:guid}", DeleteAsync);

        return routes;
    }

    private static Task<List<AlertRuleResponse>> ListAsync(ClaimsPrincipal user, ArgusDbContext db, CancellationToken cancellationToken)
    {
        var ownerId = user.GetUserId();
        return Project(db, db.AlertRules.Where(rule => rule.OwnerId == ownerId).OrderBy(rule => rule.Name)).ToListAsync(cancellationToken);
    }

    private static async Task<Results<Ok<AlertRuleResponse>, NotFound>> GetAsync(
        Guid id, ClaimsPrincipal user, ArgusDbContext db, CancellationToken cancellationToken)
    {
        var ownerId = user.GetUserId();
        var rule = await Project(db, db.AlertRules.Where(r => r.Id == id && r.OwnerId == ownerId)).SingleOrDefaultAsync(cancellationToken);
        return rule is null ? TypedResults.NotFound() : TypedResults.Ok(rule);
    }

    private static async Task<Results<Created<AlertRuleResponse>, ValidationProblem>> CreateAsync(
        AlertRuleRequest request, ClaimsPrincipal user, ArgusDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var ownerId = user.GetUserId();
        var (errors, tag) = await ValidateAsync(request, ownerId, db, cancellationToken);
        if (errors is not null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var now = time.GetUtcNow();
        var rule = new AlertRule { OwnerId = ownerId, CreatedAt = now };
        Apply(rule, request, tag, now);
        db.AlertRules.Add(rule);
        await db.SaveChangesAsync(cancellationToken);

        var created = await Project(db, db.AlertRules.Where(r => r.Id == rule.Id)).SingleAsync(cancellationToken);
        return TypedResults.Created($"/api/alert-rules/{rule.Id}", created);
    }

    private static async Task<Results<Ok<AlertRuleResponse>, NotFound, ValidationProblem>> UpdateAsync(
        Guid id, AlertRuleRequest request, ClaimsPrincipal user, ArgusDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var ownerId = user.GetUserId();
        var rule = await db.AlertRules.SingleOrDefaultAsync(r => r.Id == id && r.OwnerId == ownerId, cancellationToken);
        if (rule is null)
        {
            return TypedResults.NotFound();
        }

        var (errors, tag) = await ValidateAsync(request, ownerId, db, cancellationToken);
        if (errors is not null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // Open alerts describe the old condition. When the rule now measures something else, close them;
        // threshold and scope changes are left to the next evaluation.
        var now = time.GetUtcNow();
        if (rule.Metric != request.Metric || rule.ResourceFilter != NormalizeFilter(request.ResourceFilter))
        {
            await ResolveOpenAlertsAsync(db, rule.Id, now, cancellationToken);
        }

        Apply(rule, request, tag, now);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(await Project(db, db.AlertRules.Where(r => r.Id == rule.Id)).SingleAsync(cancellationToken));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(
        Guid id, ClaimsPrincipal user, ArgusDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var ownerId = user.GetUserId();
        var rule = await db.AlertRules.SingleOrDefaultAsync(r => r.Id == id && r.OwnerId == ownerId, cancellationToken);
        if (rule is null)
        {
            return TypedResults.NotFound();
        }

        // Past alerts stay in the history (without their rule); open ones end with it.
        await ResolveOpenAlertsAsync(db, rule.Id, time.GetUtcNow(), cancellationToken);
        db.AlertRules.Remove(rule);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static IQueryable<AlertRuleResponse> Project(ArgusDbContext db, IQueryable<AlertRule> rules) =>
        rules.Select(rule => new AlertRuleResponse(
            rule.Id,
            rule.Name,
            rule.Metric,
            rule.Operator,
            rule.Threshold,
            rule.DurationSeconds,
            rule.Severity,
            rule.HostId,
            rule.Host != null ? rule.Host.DisplayName : null,
            rule.Tag,
            rule.ResourceFilter,
            rule.Enabled,
            db.Alerts.Count(alert => alert.RuleId == rule.Id && alert.Status == AlertStatus.Firing),
            rule.CreatedAt,
            rule.UpdatedAt));

    private static async Task<(Dictionary<string, string[]>? Errors, string? Tag)> ValidateAsync(
        AlertRuleRequest request, Guid ownerId, ArgusDbContext db, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var metric = request.Metric!.Value;

        if (metric.IsPercentage() && request.Threshold is < 0 or > 100)
        {
            errors["threshold"] = ["Percentages must be between 0 and 100."];
        }
        else if (metric != AlertMetric.HostOffline && request.Threshold < 0)
        {
            errors["threshold"] = ["The threshold cannot be negative."];
        }

        if (metric == AlertMetric.HostOffline && request.DurationSeconds < 60)
        {
            errors["durationSeconds"] = ["Allow at least 60 seconds, so brief network hiccups do not count as outages."];
        }

        if (!metric.IsPerFilesystem() && !string.IsNullOrWhiteSpace(request.ResourceFilter))
        {
            errors["resourceFilter"] = ["Only disk and inode rules can target a mount point."];
        }

        string? tag = null;
        if (request.HostId is not null && !string.IsNullOrWhiteSpace(request.Tag))
        {
            errors["tag"] = ["Choose either a host or a tag, not both."];
        }
        else if (!string.IsNullOrWhiteSpace(request.Tag))
        {
            if (HostTags.TryNormalize([request.Tag], out var normalized, out var error))
            {
                tag = normalized[0];
            }
            else
            {
                errors["tag"] = [error];
            }
        }

        if (request.HostId is { } hostId && !await db.Hosts.AnyAsync(h => h.Id == hostId && h.OwnerId == ownerId, cancellationToken))
        {
            errors["hostId"] = ["Choose one of your own hosts."];
        }

        return (errors.Count == 0 ? null : errors, tag);
    }

    private static void Apply(AlertRule rule, AlertRuleRequest request, string? tag, DateTimeOffset now)
    {
        var metric = request.Metric!.Value;
        rule.Name = request.Name.Trim();
        rule.Metric = metric;

        // "Offline" has no threshold of its own: it fires once the host is silent for the duration.
        rule.Operator = metric == AlertMetric.HostOffline ? AlertOperator.Above : request.Operator;
        rule.Threshold = metric == AlertMetric.HostOffline ? 0 : request.Threshold;
        rule.DurationSeconds = request.DurationSeconds;
        rule.Severity = request.Severity;
        rule.HostId = request.HostId;
        rule.Tag = tag;
        rule.ResourceFilter = NormalizeFilter(request.ResourceFilter);
        rule.Enabled = request.Enabled;
        rule.UpdatedAt = now;
    }

    private static string? NormalizeFilter(string? filter) => string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();

    private static Task<int> ResolveOpenAlertsAsync(ArgusDbContext db, Guid ruleId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Alerts
            .Where(alert => alert.RuleId == ruleId && alert.Status == AlertStatus.Firing)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(alert => alert.Status, AlertStatus.Resolved)
                .SetProperty(alert => alert.ResolvedAt, now), cancellationToken);
}
