using System.Security.Claims;
using Argus.Server.Data;
using Argus.Server.Features.Agents;
using Argus.Server.Features.Hosts;
using Argus.Server.Features.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Argus.Server.Features.Summary;

/// <summary>Long-term totals and averages for the hosts a user can see, by day, month or year.</summary>
public static class SummaryEndpoints
{
    private const string DefaultTimeZone = "UTC";

    public static IEndpointRouteBuilder MapSummaryEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/summary", GetAsync).WithTags("Summary");
        return routes;
    }

    /// <summary>Periods each request may cover by default, and at most.</summary>
    private static (int Default, int Max) Counts(SummaryPeriod period) => period switch
    {
        SummaryPeriod.Day => (30, 366),
        SummaryPeriod.Month => (12, 36),
        _ => (3, 10),
    };

    private static async Task<Results<Ok<UsageSummary>, ValidationProblem>> GetAsync(
        SummaryPeriod? period,
        int? count,
        string? timeZone,
        ClaimsPrincipal user,
        ArgusDbContext db,
        SummaryQueries queries,
        IOptions<AgentOptions> agents,
        IOptions<RetentionOptions> retention,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var kind = period ?? SummaryPeriod.Day;
        var (defaultCount, maxCount) = Counts(kind);
        var periods = count ?? defaultCount;
        var zone = string.IsNullOrWhiteSpace(timeZone) ? DefaultTimeZone : timeZone.Trim();

        var errors = new Dictionary<string, string[]>();
        if (periods < 1 || periods > maxCount)
        {
            errors["count"] = [$"'count' must be between 1 and {maxCount} for {kind.ToString().ToLowerInvariant()}s."];
        }

        if (zone.Length > 64 || !await queries.IsTimeZoneAsync(zone, cancellationToken))
        {
            errors["timeZone"] = ["'timeZone' must be a time zone name such as 'Europe/London'."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var hosts = await db.Hosts.AsNoTracking().VisibleTo(user)
            .OrderBy(host => host.DisplayName)
            .Select(host => new { host.Id, host.DisplayName, host.CreatedAt })
            .ToListAsync(cancellationToken);

        var usage = await queries.GetAsync(
            hosts.ToDictionary(host => host.Id, host => host.CreatedAt),
            kind,
            periods,
            zone,
            TimeSpan.FromSeconds(agents.Value.CollectionIntervalSeconds),
            time.GetUtcNow(),
            cancellationToken);

        return TypedResults.Ok(new UsageSummary(
            kind,
            zone,
            retention.Value.HourlyDays,
            hosts.Select(host => new SummaryHost(host.Id, host.DisplayName)).ToList(),
            usage));
    }
}
