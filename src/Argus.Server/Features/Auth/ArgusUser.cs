using Microsoft.AspNetCore.Identity;

namespace Argus.Server.Features.Auth;

public sealed class ArgusUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Set when an administrator disables the account; disabled users cannot sign in.</summary>
    public DateTimeOffset? DisabledAt { get; set; }
}
