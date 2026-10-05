using Argus.Server.Features.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Argus.Server.Features.ApiTokens;

/// <summary>
/// A read-only key a user gives another program, such as Home Assistant, so it can read what the user
/// sees in Argus. Only a hash is stored; the token itself is shown once when it is created.
/// </summary>
public sealed class ApiToken
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public ArgusUser? Owner { get; set; }

    public string Name { get; set; } = "";

    public string TokenHash { get; set; } = "";

    /// <summary>The start of the token (not secret), to help people recognise it later.</summary>
    public string TokenPrefix { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Roughly when the token was last used; updated at most every few minutes.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}

internal sealed class ApiTokenConfiguration : IEntityTypeConfiguration<ApiToken>
{
    public void Configure(EntityTypeBuilder<ApiToken> token)
    {
        token.ToTable("api_tokens");

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
