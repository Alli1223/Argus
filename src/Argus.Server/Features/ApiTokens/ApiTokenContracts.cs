using System.ComponentModel.DataAnnotations;

namespace Argus.Server.Features.ApiTokens;

public sealed record ApiTokenSummary(
    Guid Id,
    string Name,
    string TokenPrefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt,
    bool IsActive);

public sealed record CreateApiTokenRequest
{
    [Required, MaxLength(100)]
    public string Name { get; init; } = "";
}

/// <summary>Returned once, when a token is created: the only time the secret is visible.</summary>
public sealed record CreatedApiToken(string Token, ApiTokenSummary Summary);
