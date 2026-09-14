using System.Security.Claims;
using Argus.Server.Features.Auth;
using Argus.Server.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace Argus.Server.Features.Account;

/// <summary>Self-service endpoints for the signed-in user.</summary>
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder routes)
    {
        var account = routes.MapGroup("/account").WithTags("Account");

        account.MapPut("/profile", UpdateProfileAsync);
        account.MapPost("/password", ChangePasswordAsync).RequireRateLimiting(RateLimiting.AuthPolicy);

        return routes;
    }

    private static async Task<Results<Ok<CurrentUserResponse>, ValidationProblem, UnauthorizedHttpResult>> UpdateProfileAsync(
        UpdateProfileRequest request, ClaimsPrincipal principal, UserManager<ArgusUser> users)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        user.DisplayName = request.DisplayName.Trim();
        var result = await users.UpdateAsync(user);
        return result.Succeeded
            ? TypedResults.Ok(await UserAccounts.ToResponseAsync(user, users))
            : UserAccounts.IdentityValidationProblem(result);
    }

    private static async Task<Results<NoContent, ValidationProblem, UnauthorizedHttpResult>> ChangePasswordAsync(
        ChangePasswordRequest request, ClaimsPrincipal principal, UserManager<ArgusUser> users, SignInManager<ArgusUser> signIn)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return TypedResults.ValidationProblem(result.Errors
                .GroupBy(error => error.Code == nameof(IdentityErrorDescriber.PasswordMismatch) ? "currentPassword" : "newPassword")
                .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()));
        }

        // Changing the password rotated the security stamp, which signs out every other session.
        // Re-issue this session's cookie so the user who made the change stays signed in.
        await signIn.RefreshSignInAsync(user);
        return TypedResults.NoContent();
    }
}
