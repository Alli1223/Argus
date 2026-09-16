using Argus.Server.Data;
using Argus.Server.Features.Alerts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Argus.Server.Tests.Infrastructure;

/// <summary>A server whose clock the tests control, with alert evaluation on demand.</summary>
public class AlertsFixture(PostgresFixture postgres) : ArgusAppFixture(postgres)
{
    /// <summary>Starts at the current time (to the second) so the database's own clock agrees closely.</summary>
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

    protected override void ConfigureServices(IServiceCollection services) => services.AddSingleton<TimeProvider>(Time);

    public Task<AlertEvaluationResult> EvaluateAsync() =>
        WithScopeAsync(services => services.GetRequiredService<AlertEvaluator>().EvaluateAsync(TestContext.Current.CancellationToken));

    public Task<AlertRule> AddRuleAsync(Guid ownerId, AlertMetric metric, double threshold, int durationSeconds = 0, Action<AlertRule>? configure = null) =>
        WithScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ArgusDbContext>();
            var rule = new AlertRule
            {
                OwnerId = ownerId,
                Name = $"{metric} {threshold}",
                Metric = metric,
                Operator = AlertOperator.Above,
                Threshold = threshold,
                DurationSeconds = durationSeconds,
                Severity = AlertSeverity.Critical,
                Enabled = true,
                CreatedAt = Time.GetUtcNow(),
                UpdatedAt = Time.GetUtcNow(),
            };
            configure?.Invoke(rule);
            db.AlertRules.Add(rule);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return rule;
        });

    public Task<List<Alert>> AlertsForHostAsync(Guid hostId) =>
        WithScopeAsync(services => services.GetRequiredService<ArgusDbContext>().Alerts
            .AsNoTracking()
            .Where(alert => alert.HostId == hostId)
            .OrderBy(alert => alert.FiredAt)
            .ToListAsync(TestContext.Current.CancellationToken));
}
