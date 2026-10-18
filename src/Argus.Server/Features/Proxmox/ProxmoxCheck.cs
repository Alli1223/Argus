using Argus.Server.Features.Hosts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Argus.Server.Features.Proxmox;

/// <summary>
/// The newest report of a Proxmox VE node's guests, one per host. The guests are kept as JSON: they are
/// only ever read and replaced together.
/// </summary>
public sealed class ProxmoxCheck
{
    public Guid HostId { get; set; }

    public MonitoredHost? Host { get; set; }

    public DateTimeOffset CheckedAt { get; set; }

    public string? Node { get; set; }

    public string? Version { get; set; }

    /// <summary>Why the agent could not read Proxmox VE at its last report; the guests are then those seen before.</summary>
    public string? Problem { get; set; }

    public bool ActionsEnabled { get; set; }

    /// <summary>A JSON array of <see cref="ProxmoxGuestInfo"/>.</summary>
    public string Guests { get; set; } = "[]";
}

internal sealed class ProxmoxCheckConfiguration : IEntityTypeConfiguration<ProxmoxCheck>
{
    public void Configure(EntityTypeBuilder<ProxmoxCheck> check)
    {
        check.ToTable("proxmox_checks");
        check.HasKey(c => c.HostId);
        check.HasOne(c => c.Host).WithOne().HasForeignKey<ProxmoxCheck>(c => c.HostId).OnDelete(DeleteBehavior.Cascade);

        check.Property(c => c.Node).HasMaxLength(256);
        check.Property(c => c.Version).HasMaxLength(64);
        check.Property(c => c.Problem).HasMaxLength(1000);
        check.Property(c => c.Guests).HasColumnType("jsonb");
    }
}
