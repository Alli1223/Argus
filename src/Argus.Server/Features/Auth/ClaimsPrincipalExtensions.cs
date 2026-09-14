using System.Security.Claims;

namespace Argus.Server.Features.Auth;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The id of the signed-in user (browser sessions).</summary>
    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("The principal has no user id."));

    public static bool IsAdmin(this ClaimsPrincipal principal) => principal.IsInRole(Roles.Admin);
}
