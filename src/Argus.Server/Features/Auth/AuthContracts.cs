using System.ComponentModel.DataAnnotations;

namespace Argus.Server.Features.Auth;

public sealed record AuthStatusResponse(bool SetupRequired, bool RegistrationEnabled);

public sealed record CurrentUserResponse(Guid Id, string Email, string DisplayName, IReadOnlyList<string> Roles, bool IsAdmin);

/// <summary>Creates an account: the first administrator during setup, or a user via self-registration.</summary>
public sealed record NewAccountRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = "";

    [Required, MaxLength(128)]
    public string Password { get; init; } = "";

    [Required, MaxLength(100)]
    public string DisplayName { get; init; } = "";
}

public sealed record LoginRequest
{
    [Required, MaxLength(256)]
    public string Email { get; init; } = "";

    [Required, MaxLength(128)]
    public string Password { get; init; } = "";

    public bool RememberMe { get; init; }
}
