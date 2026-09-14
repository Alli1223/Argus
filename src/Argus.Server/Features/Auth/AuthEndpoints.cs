using System.Security.Claims;
using Argus.Server.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Argus.Server.Features.Auth;

public static class AuthEndpoints
{
    /// <summary>Advisory lock that serialises first-run setup so only one first administrator can exist.</summary>
    private const long SetupLockKey = 0x4152_4755_5301;

    private static readonly PasswordHasher<ArgusUser> DummyHasher = new();
    private static readonly string DummyPasswordHash = DummyHasher.HashPassword(new ArgusUser(), Guid.NewGuid().ToString());

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var auth = routes.MapGroup("/auth").WithTags("Auth");

        auth.MapGet("/status", GetStatusAsync).AllowAnonymous();
        auth.MapPost("/setup", SetupAsync).AllowAnonymous();
        auth.MapPost("/login", LoginAsync).AllowAnonymous();
        auth.MapPost("/logout", LogoutAsync).AllowAnonymous();
        auth.MapGet("/me", GetCurrentUserAsync);

        return routes;
    }

    private static async Task<AuthStatusResponse> GetStatusAsync(
        ArgusDbContext db, IOptions<AuthOptions> options, CancellationToken cancellationToken) =>
        new(SetupRequired: !await db.Users.AnyAsync(cancellationToken), RegistrationEnabled: options.Value.AllowRegistration);

    private static async Task<Results<Ok<CurrentUserResponse>, ValidationProblem, ProblemHttpResult>> SetupAsync(
        NewAccountRequest request,
        ArgusDbContext db,
        UserManager<ArgusUser> users,
        SignInManager<ArgusUser> signIn,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({SetupLockKey})", cancellationToken);

        if (await db.Users.AnyAsync(cancellationToken))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Setup already completed",
                detail: "An administrator account already exists. Sign in instead.");
        }

        var user = NewUser(request, time);
        var result = await users.CreateAsync(user, request.Password);
        if (result.Succeeded)
        {
            result = await users.AddToRoleAsync(user, Roles.Admin);
        }

        if (!result.Succeeded)
        {
            return IdentityValidationProblem(result);
        }

        await transaction.CommitAsync(cancellationToken);
        await signIn.SignInAsync(user, isPersistent: true);
        return TypedResults.Ok(await ToResponseAsync(user, users));
    }

    private static async Task<Results<Ok<CurrentUserResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request, UserManager<ArgusUser> users, SignInManager<ArgusUser> signIn, TimeProvider time)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            // Spend as long as a real password check so response times don't reveal which accounts exist.
            DummyHasher.VerifyHashedPassword(new ArgusUser(), DummyPasswordHash, request.Password);
            return InvalidCredentials();
        }

        var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Account locked",
                detail: "Too many failed sign-in attempts. Try again in a few minutes.");
        }

        if (result.IsNotAllowed)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Account disabled",
                detail: "This account has been disabled by an administrator.");
        }

        if (!result.Succeeded)
        {
            return InvalidCredentials();
        }

        user.LastLoginAt = time.GetUtcNow();
        await users.UpdateAsync(user);
        await signIn.SignInAsync(user, request.RememberMe);
        return TypedResults.Ok(await ToResponseAsync(user, users));
    }

    private static async Task<NoContent> LogoutAsync(SignInManager<ArgusUser> signIn)
    {
        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<CurrentUserResponse>, UnauthorizedHttpResult>> GetCurrentUserAsync(
        ClaimsPrincipal principal, UserManager<ArgusUser> users)
    {
        var user = await users.GetUserAsync(principal);
        return user is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(await ToResponseAsync(user, users));
    }

    internal static ArgusUser NewUser(NewAccountRequest request, TimeProvider time)
    {
        var email = request.Email.Trim();
        return new ArgusUser
        {
            UserName = email,
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            CreatedAt = time.GetUtcNow(),
        };
    }

    internal static async Task<CurrentUserResponse> ToResponseAsync(ArgusUser user, UserManager<ArgusUser> users)
    {
        var roles = (await users.GetRolesAsync(user)).Order(StringComparer.Ordinal).ToList();
        return new CurrentUserResponse(user.Id, user.Email ?? "", user.DisplayName, roles, roles.Contains(Roles.Admin));
    }

    /// <summary>Maps Identity errors onto the request fields they concern.</summary>
    internal static ValidationProblem IdentityValidationProblem(IdentityResult result) =>
        TypedResults.ValidationProblem(result.Errors
            // The user name is the email address, so the email errors already cover these.
            .Where(error => error.Code is not (nameof(IdentityErrorDescriber.DuplicateUserName)
                or nameof(IdentityErrorDescriber.InvalidUserName)))
            .GroupBy(error => error.Code switch
            {
                _ when error.Code.StartsWith("Password", StringComparison.Ordinal) => "password",
                _ when error.Code.Contains("Email", StringComparison.Ordinal) => "email",
                _ => "",
            })
            .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()));

    private static ProblemHttpResult InvalidCredentials() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Sign-in failed",
            detail: "Invalid email or password.");
}
