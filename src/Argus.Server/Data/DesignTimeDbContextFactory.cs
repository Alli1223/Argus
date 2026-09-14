using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Argus.Server.Data;

/// <summary>Lets <c>dotnet ef</c> create the context without starting the web host.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ArgusDbContext>
{
    public ArgusDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable($"ConnectionStrings__{DatabaseExtensions.ConnectionStringName}")
            ?? "Host=localhost;Port=5432;Database=argus;Username=argus;Password=argus";

        var options = new DbContextOptionsBuilder<ArgusDbContext>()
            .UseArgusNpgsql(NpgsqlDataSource.Create(connectionString))
            .Options;

        return new ArgusDbContext((DbContextOptions<ArgusDbContext>)options);
    }
}
