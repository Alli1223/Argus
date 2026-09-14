using System.ComponentModel.DataAnnotations;
using Argus.Server.Features.Hosts;

namespace Argus.Server.Features.Enrollment;

public sealed record EnrollmentTokenSummary(
    Guid Id,
    string Name,
    string TokenPrefix,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    int? MaxUses,
    int UseCount,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt,
    bool IsActive);

public sealed record CreateEnrollmentTokenRequest
{
    [Required, MaxLength(100)]
    public string Name { get; init; } = "";

    /// <summary>Hours until the token expires; omit for a token that never expires.</summary>
    [Range(1, 24 * 365)]
    public int? ExpiresInHours { get; init; }

    /// <summary>How many hosts may register with the token; omit for no limit.</summary>
    [Range(1, 100_000)]
    public int? MaxUses { get; init; }

    [MaxLength(HostTags.MaxTags)]
    public List<string> Tags { get; init; } = [];
}

/// <summary>Returned once, when a token is created: the only time the secret is visible.</summary>
public sealed record CreatedEnrollmentToken(string Token, EnrollmentTokenSummary Summary);
