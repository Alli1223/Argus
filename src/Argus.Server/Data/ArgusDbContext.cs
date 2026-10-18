using Argus.Server.Features.Alerts;
using Argus.Server.Features.ApiTokens;
using Argus.Server.Features.Auth;
using Argus.Server.Features.Enrollment;
using Argus.Server.Features.Hosts;
using Argus.Server.Features.Notifications;
using Argus.Server.Features.Power;
using Argus.Server.Features.Proxmox;
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

    public DbSet<MonitoredHost> Hosts => Set<MonitoredHost>();

    public DbSet<EnrollmentToken> EnrollmentTokens => Set<EnrollmentToken>();

    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();

    public DbSet<AlertRule> AlertRules => Set<AlertRule>();

    public DbSet<Alert> Alerts => Set<Alert>();

    public DbSet<AlertIgnore> AlertIgnores => Set<AlertIgnore>();

    public DbSet<NotificationChannel> NotificationChannels => Set<NotificationChannel>();

    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();

    public DbSet<PowerAction> PowerActions => Set<PowerAction>();

    public DbSet<ProxmoxCheck> ProxmoxChecks => Set<ProxmoxCheck>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresExtension("timescaledb");
        modelBuilder.ConfigureIdentity();
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ArgusDbContext).Assembly);
    }
}
