using Argus.Server.Features.Auth;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Argus.Server.Data;

public sealed class ArgusDbContext(DbContextOptions<ArgusDbContext> options)
    : IdentityDbContext<ArgusUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    /// <summary>ASP.NET Core data protection key ring (encrypts auth cookies), shared across restarts.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresExtension("timescaledb");
        modelBuilder.ConfigureIdentity();
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ArgusDbContext).Assembly);
    }
}
