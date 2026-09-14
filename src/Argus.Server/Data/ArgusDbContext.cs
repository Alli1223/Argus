using Microsoft.EntityFrameworkCore;

namespace Argus.Server.Data;

public sealed class ArgusDbContext(DbContextOptions<ArgusDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresExtension("timescaledb");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ArgusDbContext).Assembly);
    }
}
