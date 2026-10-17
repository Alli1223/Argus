using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using Argus.Server.Features.Auth;
using Argus.Server.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace Argus.Server.Features.Account;

public sealed record TwoFactorStatusResponse(bool Enabled, int RecoveryCodesLeft);

/// <summary>What an authenticator app needs: the key to type in, and the same as a link for a QR code.</summary>
public sealed record TwoFactorSetupResponse(string SharedKey, string AuthenticatorUri);

public sealed record TwoFactorRecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);

public sealed record EnableTwoFactorRequest([property: Required, MaxLength(16)] string Code);

/// <summary>Turning two-factor sign-in off, or replacing the recovery codes, asks for the password again.</summary>
public sealed record ConfirmPasswordRequest([property: Required, MaxLength(128)] string Password);

/// <summary>
/// Two-factor sign-in with an authenticator app (TOTP), set up by each user for their own account.
/// Recovery codes stand in for the app once each, for when the phone is lost.
/// </summary>
public static class TwoFactorEndpoints
{
    public const string Issuer = "Argus";
    public const int RecoveryCodeCount = 10;

    public static IEndpointRouteBuilder MapTwoFactorEndpoints(this IEndpointRouteBuilder routes)
    {
        var twoFactor = routes.MapGroup("/account/two-factor").WithTags("Account");

        twoFactor.MapGet("/", GetStatusAsync);
        twoFactor.MapPost("/setup", SetupAsync).RequireRateLimiting(RateLimiting.AuthPolicy);
        twoFactor.MapPost("/enable", EnableAsync).RequireRateLimiting(RateLimiting.AuthPolicy);
        twoFactor.MapPost("/disable", DisableAsync).RequireRateLimiting(RateLimiting.AuthPolicy);
        twoFactor.MapPost("/recovery-codes", RegenerateCodesAsync).RequireRateLimiting(RateLimiting.AuthPolicy);

        return routes;
    }

    private static async Task<Results<Ok<TwoFactorStatusResponse>, UnauthorizedHttpResult>> GetStatusAsync(
        ClaimsPrincipal principal, UserManager<ArgusUser> users)
    {
        var user = await users.GetUserAsync(principal);
        return user is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(new TwoFactorStatusResponse(
                await users.GetTwoFactorEnabledAsync(user), await users.CountRecoveryCodesAsync(user)));
    }

    /// <summary>Starts setting up with a new key. Nothing changes at sign-in until a code from the app is confirmed.</summary>
    private static async Task<Results<Ok<TwoFactorSetupResponse>, UnauthorizedHttpResult, ProblemHttpResult>> SetupAsync(
        ClaimsPrincipal principal, UserManager<ArgusUser> users)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (await users.GetTwoFactorEnabledAsync(user))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Two-factor sign-in is already on",
                detail: "Turn it off first to move it to another app.");
        }

        await users.ResetAuthenticatorKeyAsync(user);
        var key = (await users.GetAuthenticatorKeyAsync(user))!;
        var account = Uri.EscapeDataString(user.Email ?? user.UserName ?? user.Id.ToString());
        var uri = $"otpauth://totp/{Issuer}:{account}?secret={key}&issuer={Issuer}&digits=6";
        return TypedResults.Ok(new TwoFactorSetupResponse(FormatKey(key), uri));
    }

    /// <summary>Turns two-factor sign-in on once the app shows the right code, and hands out recovery codes.</summary>
    private static async Task<Results<Ok<TwoFactorRecoveryCodesResponse>, UnauthorizedHttpResult, ValidationProblem, ProblemHttpResult>> EnableAsync(
        EnableTwoFactorRequest request, ClaimsPrincipal principal, UserManager<ArgusUser> users, SignInManager<ArgusUser> signIn)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (await users.GetAuthenticatorKeyAsync(user) is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict, title: "Not set up yet", detail: "Start setting up two-factor sign-in first.");
        }

        var code = NormalizeCode(request.Code);
        if (!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["code"] = ["That code is not right. Enter the 6 digits the app shows now; check the phone's clock if it keeps failing."],
            });
        }

        await users.SetTwoFactorEnabledAsync(user, true);
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount);

        // Turning it on rotated the security stamp, signing out every other session; this one stays.
        await signIn.RefreshSignInAsync(user);
        return TypedResults.Ok(new TwoFactorRecoveryCodesResponse([.. codes!]));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ValidationProblem>> DisableAsync(
        ConfirmPasswordRequest request, ClaimsPrincipal principal, UserManager<ArgusUser> users, SignInManager<ArgusUser> signIn)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            return WrongPassword();
        }

        await TurnOffAsync(users, user);
        await signIn.RefreshSignInAsync(user);
        return TypedResults.NoContent();
    }

    /// <summary>New recovery codes, for when the old ones are used up or lost. The old ones stop working.</summary>
    private static async Task<Results<Ok<TwoFactorRecoveryCodesResponse>, UnauthorizedHttpResult, ValidationProblem, ProblemHttpResult>> RegenerateCodesAsync(
        ConfirmPasswordRequest request, ClaimsPrincipal principal, UserManager<ArgusUser> users)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            return WrongPassword();
        }

        if (!await users.GetTwoFactorEnabledAsync(user))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict, title: "Two-factor sign-in is off", detail: "Recovery codes come with two-factor sign-in.");
        }

        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount);
        return TypedResults.Ok(new TwoFactorRecoveryCodesResponse([.. codes!]));
    }

    /// <summary>Turns two-factor sign-in off and forgets the key, so setting it up again starts afresh.</summary>
    internal static async Task TurnOffAsync(UserManager<ArgusUser> users, ArgusUser user)
    {
        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);
    }

    /// <summary>Authenticator codes as people type them: "123 456" and "123-456" are 123456.</summary>
    internal static string NormalizeCode(string code) => code.Replace(" ", "").Replace("-", "").Trim();

    private static ValidationProblem WrongPassword() =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["password"] = ["That is not your password."] });

    /// <summary>The key in groups of four, easier to type into an app by hand.</summary>
    private static string FormatKey(string key)
    {
        var formatted = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
        {
            if (i > 0)
            {
                formatted.Append(' ');
            }

            formatted.Append(key.AsSpan(i, Math.Min(4, key.Length - i)));
        }

        return formatted.ToString().ToLowerInvariant();
    }
}
