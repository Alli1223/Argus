using System.ComponentModel.DataAnnotations;

namespace Argus.Server.Features.Account;

public sealed record UpdateProfileRequest
{
    [Required, MaxLength(100)]
    public string DisplayName { get; init; } = "";
}

public sealed record ChangePasswordRequest
{
    [Required, MaxLength(128)]
    public string CurrentPassword { get; init; } = "";

    [Required, MaxLength(128)]
    public string NewPassword { get; init; } = "";
}
