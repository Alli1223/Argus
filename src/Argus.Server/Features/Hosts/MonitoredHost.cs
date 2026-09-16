using Argus.Contracts.Agent;
using Argus.Server.Features.Auth;
using Argus.Server.Features.Updates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Argus.Server.Features.Hosts;

/// <summary>A machine running the Argus agent.</summary>
public sealed class MonitoredHost
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public ArgusUser? Owner { get; set; }

    /// <summary>Name shown in the UI; starts as the hostname and can be changed by the owner.</summary>
    public string DisplayName { get; set; } = "";

    public string Hostname { get; set; } = "";

    /// <summary>Hashed machine identifier reported by the agent; re-registering the same machine re-links this host.</summary>
    public string MachineId { get; set; } = "";

    public HostPlatform Platform { get; set; }

    public string? OsName { get; set; }

    public string? OsVersion { get; set; }

    public string? KernelVersion { get; set; }

    public string Architecture { get; set; } = "";

    public string? CpuModel { get; set; }

    public int? CpuCores { get; set; }

    public int CpuLogicalProcessors { get; set; }

    public long MemoryTotalBytes { get; set; }

    public DateTimeOffset? BootTime { get; set; }

    public List<string> IpAddresses { get; set; } = [];

    public string AgentVersion { get; set; } = "";

    public List<string> Tags { get; set; } = [];

    public string? Notes { get; set; }

    /// <summary>SHA-256 of the host's agent key. The key itself is only ever known to the agent.</summary>
    public string AgentKeyHash { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }

    public DateTimeOffset? InventoryUpdatedAt { get; set; }

    /// <summary>The agent version someone asked this host to update to, until the agent runs it or gives up.</summary>
    public string? AgentUpdateVersion { get; set; }

    public DateTimeOffset? AgentUpdateRequestedAt { get; set; }

    /// <summary>Why the latest agent update failed.</summary>
    public string? AgentUpdateError { get; set; }

    /// <summary>Copies agent-reported inventory onto the host, clipping values that would not fit.</summary>
    public void ApplyInventory(SystemInfo info, string agentVersion, DateTimeOffset now)
    {
        Hostname = Clip(info.Hostname, MonitoredHostConfiguration.NameLength);
        Platform = info.Platform;
        OsName = ClipOptional(info.OsName, MonitoredHostConfiguration.NameLength);
        OsVersion = ClipOptional(info.OsVersion, MonitoredHostConfiguration.VersionLength);
        KernelVersion = ClipOptional(info.KernelVersion, MonitoredHostConfiguration.VersionLength);
        Architecture = Clip(info.Architecture, MonitoredHostConfiguration.ArchitectureLength);
        CpuModel = ClipOptional(info.CpuModel, MonitoredHostConfiguration.NameLength);
        CpuCores = info.CpuCores;
        CpuLogicalProcessors = info.CpuLogicalProcessors;
        MemoryTotalBytes = info.MemoryTotalBytes;
        BootTime = info.BootTime?.ToUniversalTime();
        IpAddresses = info.IpAddresses
            .Take(AgentLimits.MaxIpAddresses)
            .Select(address => Clip(address, MonitoredHostConfiguration.AddressLength))
            .ToList();
        AgentVersion = Clip(agentVersion, MonitoredHostConfiguration.VersionLength);

        // An agent that reports the version it was asked to update to has finished updating.
        if (AgentUpdateVersion is not null && !ReleaseVersions.IsNewer(AgentUpdateVersion, AgentVersion))
        {
            AgentUpdateVersion = null;
            AgentUpdateRequestedAt = null;
            AgentUpdateError = null;
        }
        InventoryUpdatedAt = now;
    }

    private static string Clip(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string? ClipOptional(string? value, int maxLength) =>
        value is null ? null : Clip(value, maxLength);
}

internal sealed class MonitoredHostConfiguration : IEntityTypeConfiguration<MonitoredHost>
{
    public const int NameLength = 256;
    public const int VersionLength = 128;
    public const int ArchitectureLength = 32;
    public const int AddressLength = 64;
    public const int ErrorLength = 1000;

    public void Configure(EntityTypeBuilder<MonitoredHost> host)
    {
        host.ToTable("hosts");

        host.HasOne(h => h.Owner)
            .WithMany()
            .HasForeignKey(h => h.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        host.Property(h => h.DisplayName).HasMaxLength(NameLength);
        host.Property(h => h.Hostname).HasMaxLength(NameLength);
        host.Property(h => h.MachineId).HasMaxLength(128);
        host.Property(h => h.Platform).HasConversion<string>().HasMaxLength(16);
        host.Property(h => h.OsName).HasMaxLength(NameLength);
        host.Property(h => h.OsVersion).HasMaxLength(VersionLength);
        host.Property(h => h.KernelVersion).HasMaxLength(VersionLength);
        host.Property(h => h.Architecture).HasMaxLength(ArchitectureLength);
        host.Property(h => h.CpuModel).HasMaxLength(NameLength);
        host.Property(h => h.AgentVersion).HasMaxLength(VersionLength);
        host.Property(h => h.AgentUpdateVersion).HasMaxLength(VersionLength);
        host.Property(h => h.AgentUpdateError).HasMaxLength(ErrorLength);
        host.Property(h => h.Notes).HasMaxLength(4000);
        host.Property(h => h.AgentKeyHash).HasMaxLength(64);

        host.HasIndex(h => h.AgentKeyHash).IsUnique();
        host.HasIndex(h => new { h.OwnerId, h.MachineId }).IsUnique();
        host.HasIndex(h => h.Tags).HasMethod("gin");
    }
}
