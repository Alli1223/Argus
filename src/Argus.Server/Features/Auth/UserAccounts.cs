using Argus.Server.Data;
using Argus.Server.Features.Alerts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace Argus.Server.Features.Auth;

/// <summary>Account helpers shared by setup, self-registration and user administration.</summary>
internal static class UserAccounts
{
    /// <summary>
    /// Creates a user with a role and the default alert rules. Callers wrap this in a transaction so
    /// every step succeeds or none does.
    /// </summary>
    public static async Task<(ArgusUser? User, IdentityResult Result)> CreateAsync(
        ArgusDbContext db, UserManager<ArgusUser> users, NewAccountRequest request, string role, TimeProvider time)
    {
        var email = request.Email.Trim();
        var user = new ArgusUser
        {
            UserName = email,
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            CreatedAt = time.GetUtcNow(),
        };

        var result = await users.CreateAsync(user, request.Password);
        if (result.Succeeded)
        {
            result = await users.AddToRoleAsync(user, role);
        }

        if (!result.Succeeded)
        {
            return (null, result);
        }

        db.AlertRules.AddRange(DefaultAlertRules.For(user.Id, time.GetUtcNow()));
        await db.SaveChangesAsync();
        return (user, result);
    }

    public static async Task<CurrentUserResponse> ToResponseAsync(ArgusUser user, UserManager<ArgusUser> users)
    {
        var roles = (await users.GetRolesAsync(user)).Order(StringComparer.Ordinal).ToList();
        return new CurrentUserResponse(user.Id, user.Email ?? "", user.DisplayName, roles, roles.Contains(Roles.Admin));
    }

    /// <summary>Maps Identity errors onto the request fields they concern.</summary>
    public static ValidationProblem IdentityValidationProblem(IdentityResult result, string passwordField = "password") =>
        TypedResults.ValidationProblem(result.Errors
            // The user name is the email address, so the email errors already cover these.
            .Where(error => error.Code is not (nameof(IdentityErrorDescriber.DuplicateUserName)
                or nameof(IdentityErrorDescriber.InvalidUserName)))
            .GroupBy(error => error.Code switch
            {
                _ when error.Code.StartsWith("Password", StringComparison.Ordinal) => passwordField,
                _ when error.Code.Contains("Email", StringComparison.Ordinal) => "email",
                _ => "",
            })
            .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()));
}
