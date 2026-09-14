using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Argus.Server.Data;

public static class DatabaseInitializer
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Waits for the database, applies pending migrations (unless disabled) and verifies that the
    /// TimescaleDB extension is available. Runs before the server starts accepting requests.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        var options = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer).FullName!);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await InitializeAsync(app.Services, options, logger, cancellationToken);
                return;
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < options.StartupRetries)
            {
                logger.LogWarning("Database not reachable yet (attempt {Attempt}/{MaxAttempts}): {Reason}",
                    attempt, options.StartupRetries, ex.Message);
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
    }

    private static async Task InitializeAsync(
        IServiceProvider services, DatabaseOptions options, ILogger logger, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArgusDbContext>();

        if (options.MigrateOnStartup)
        {
            // Asking for pending migrations before the history table exists makes EF log a failed
            // query, so on a fresh database every migration is pending.
            var historyExists = await db.GetService<IHistoryRepository>().ExistsAsync(cancellationToken);
            var pending = historyExists
                ? (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList()
                : db.Database.GetMigrations().ToList();
            if (pending.Count > 0)
            {
                logger.LogInformation("Applying {Count} database migration(s): {Migrations}",
                    pending.Count, string.Join(", ", pending));
                await db.Database.MigrateAsync(cancellationToken);
            }
        }

        var dataSource = services.GetRequiredService<NpgsqlDataSource>();
        await using var command = dataSource.CreateCommand(
            "SELECT extversion FROM pg_extension WHERE extname = 'timescaledb'");
        var version = await command.ExecuteScalarAsync(cancellationToken) as string
            ?? throw new InvalidOperationException(
                "The TimescaleDB extension is not installed in the Argus database. " +
                "Use the timescale/timescaledb image or install the extension (https://docs.timescale.com).");

        logger.LogInformation("Database ready (TimescaleDB {Version})", version);
    }

    private static bool IsTransient(Exception ex) =>
        ex is NpgsqlException { IsTransient: true } or TimeoutException
        || ex.InnerException is NpgsqlException { IsTransient: true };
}
