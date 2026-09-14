using Npgsql;

namespace Argus.Server.Features.Metrics;

/// <summary>
/// Keeps TimescaleDB retention policies in line with configuration. Policies are replaced on every
/// start, so changing <see cref="RetentionOptions"/> takes effect after a restart.
/// </summary>
public static class RetentionPolicies
{
    public static async Task ApplyAsync(NpgsqlDataSource dataSource, RetentionOptions options, CancellationToken cancellationToken)
    {
        (string Relation, int Days)[] policies =
        [
            ("host_metrics", options.RawDays),
            ("filesystem_metrics", options.RawDays),
            ("network_metrics", options.RawDays),
            ("host_metrics_5m", options.FiveMinuteDays),
            ("host_metrics_1h", options.HourlyDays),
            ("filesystem_metrics_1h", options.HourlyDays),
            ("network_metrics_1h", options.HourlyDays),
        ];

        await using var batch = dataSource.CreateBatch();
        foreach (var (relation, days) in policies)
        {
            batch.BatchCommands.Add(new NpgsqlBatchCommand("SELECT remove_retention_policy(@relation::regclass, if_exists => true)")
            {
                Parameters = { new NpgsqlParameter<string>("relation", relation) },
            });
            batch.BatchCommands.Add(new NpgsqlBatchCommand(
                "SELECT add_retention_policy(@relation::regclass, drop_after => make_interval(days => @days))")
            {
                Parameters = { new NpgsqlParameter<string>("relation", relation), new NpgsqlParameter<int>("days", days) },
            });
        }

        await batch.ExecuteNonQueryAsync(cancellationToken);
    }
}
