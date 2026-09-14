using Argus.Server.Features.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Argus.Server.Features.Enrollment;

/// <summary>
/// A secret a user hands to new agents so they can register hosts on the user's behalf.
/// Only a hash is stored; the token itself is shown once when it is created.
/// </summary>
public sealed class EnrollmentToken
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public ArgusUser? Owner { get; set; }

    public string Name { get; set; } = "";

    public string TokenHash { get; set; } = "";

    /// <summary>The start of the token (not secret), to help people recognise it later.</summary>
    public string TokenPrefix { get; set; } = "";

    /// <summary>Tags given to every host registered with this token.</summary>
    public List<string> Tags { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public int? MaxUses { get; set; }

    public int UseCount { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsUsable(DateTimeOffset now) =>
        RevokedAt is null
        && (ExpiresAt is null || ExpiresAt > now)
        && (MaxUses is null || UseCount < MaxUses);
}

internal sealed class EnrollmentTokenConfiguration : IEntityTypeConfiguration<EnrollmentToken>
{
    public void Configure(EntityTypeBuilder<EnrollmentToken> token)
    {
        token.ToTable("enrollment_tokens");

        token.HasOne(t => t.Owner)
            .WithMany()
            .HasForeignKey(t => t.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        token.Property(t => t.Name).HasMaxLength(100);
        token.Property(t => t.TokenHash).HasMaxLength(64);
        token.Property(t => t.TokenPrefix).HasMaxLength(32);

        token.HasIndex(t => t.TokenHash).IsUnique();
        token.HasIndex(t => t.OwnerId);
    }
}
