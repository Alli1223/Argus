using System.ComponentModel.DataAnnotations;
using Argus.Server.Features.Auth;

namespace Argus.Server.Features.Users;

public sealed record UserSummary(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    bool IsDisabled,
    bool IsLockedOut);

public sealed record CreateUserRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = "";

    [Required, MaxLength(100)]
    public string DisplayName { get; init; } = "";

    [Required, MaxLength(128)]
    public string Password { get; init; } = "";

    [Required, AllowedValues(Roles.Admin, Roles.User)]
    public string Role { get; init; } = Roles.User;
}

public sealed record UpdateUserRequest
{
    [Required, MaxLength(100)]
    public string DisplayName { get; init; } = "";

    [Required, AllowedValues(Roles.Admin, Roles.User)]
    public string Role { get; init; } = Roles.User;
}

public sealed record ResetPasswordRequest
{
    [Required, MaxLength(128)]
    public string NewPassword { get; init; } = "";
}
