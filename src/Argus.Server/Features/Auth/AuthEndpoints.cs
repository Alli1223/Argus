using System.Security.Claims;
using Argus.Server.Data;
using Argus.Server.Infrastructure;
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
        auth.MapPost("/setup", SetupAsync).AllowAnonymous().RequireRateLimiting(RateLimiting.AuthPolicy);
        auth.MapPost("/register", RegisterAsync).AllowAnonymous().RequireRateLimiting(RateLimiting.AuthPolicy);
        auth.MapPost("/login", LoginAsync).AllowAnonymous().RequireRateLimiting(RateLimiting.AuthPolicy);
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

        var (user, result) = await UserAccounts.CreateAsync(users, request, Roles.Admin, time);
        if (user is null)
        {
            return UserAccounts.IdentityValidationProblem(result);
        }

        await transaction.CommitAsync(cancellationToken);
        await signIn.SignInAsync(user, isPersistent: true);
        return TypedResults.Ok(await UserAccounts.ToResponseAsync(user, users));
    }

    private static async Task<Results<Ok<CurrentUserResponse>, ValidationProblem, ProblemHttpResult>> RegisterAsync(
        NewAccountRequest request,
        ArgusDbContext db,
        UserManager<ArgusUser> users,
        SignInManager<ArgusUser> signIn,
        IOptions<AuthOptions> options,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!options.Value.AllowRegistration)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Registration disabled",
                detail: "Ask an administrator to create an account for you.");
        }

        // The first account has to come from setup so that it becomes an administrator.
        if (!await db.Users.AnyAsync(cancellationToken))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Setup required",
                detail: "Argus has not been set up yet. Create the administrator account first.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var (user, result) = await UserAccounts.CreateAsync(users, request, Roles.User, time);
        if (user is null)
        {
            return UserAccounts.IdentityValidationProblem(result);
        }

        await transaction.CommitAsync(cancellationToken);
        await signIn.SignInAsync(user, isPersistent: false);
        return TypedResults.Ok(await UserAccounts.ToResponseAsync(user, users));
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
        return TypedResults.Ok(await UserAccounts.ToResponseAsync(user, users));
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
            : TypedResults.Ok(await UserAccounts.ToResponseAsync(user, users));
    }

    private static ProblemHttpResult InvalidCredentials() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Sign-in failed",
            detail: "Invalid email or password.");
}
