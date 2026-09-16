using Argus.Server.Infrastructure;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Argus.Server.Data;

public static class DatabaseExtensions
{
    public const string ConnectionStringName = "Argus";

    /// <summary>
    /// Registers a shared <see cref="NpgsqlDataSource"/> (used by EF Core and by raw SQL for time-series
    /// work) and the <see cref="ArgusDbContext"/>. The connection string is resolved lazily so tests
    /// can override configuration.
    /// </summary>
    public static IServiceCollection AddArgusDatabase(this IServiceCollection services)
    {
        services.AddValidatedOptions<DatabaseOptions>(DatabaseOptions.SectionName);

        // Raw SQL reads snake_case columns (host_id) into PascalCase properties (HostId). This is global
        // Dapper state, so it is set at startup: the alert evaluator may query before anything else does.
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        services.AddSingleton(sp =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"No database configured: set ConnectionStrings:{ConnectionStringName} (env: ConnectionStrings__{ConnectionStringName}).");
            }

            return new NpgsqlDataSourceBuilder(connectionString)
                .UseLoggerFactory(sp.GetRequiredService<ILoggerFactory>())
                .Build();
        });

        services.AddDbContext<ArgusDbContext>((sp, options) =>
            options.UseArgusNpgsql(sp.GetRequiredService<NpgsqlDataSource>()));

        return services;
    }

    internal static DbContextOptionsBuilder UseArgusNpgsql(this DbContextOptionsBuilder options, NpgsqlDataSource dataSource) =>
        options
            .UseNpgsql(dataSource, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}
