using System.Security.Claims;
using System.Text.Encodings.Web;
using Argus.Server.Data;
using Argus.Server.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Argus.Server.Features.ApiTokens;

public static class ApiTokenDefaults
{
    public const string Scheme = "ApiToken";

    /// <summary>Picks a browser session or an API token for each request.</summary>
    public const string SelectorScheme = "SessionOrApiToken";

    public const string Prefix = "argus_at_";
    public const string TokenIdClaim = "argus:api_token_id";

    private const string BearerPrefix = "Bearer " + Prefix;

    internal static bool CarriesApiToken(HttpRequest request) =>
        request.Headers.Authorization.ToString().StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Who a token acts for: its owner, with the owner's roles.</summary>
public sealed record ApiTokenIdentity(Guid TokenId, Guid UserId, string UserName, IReadOnlyList<string> Roles);

/// <summary>
/// Resolves API tokens to their owners. Lookups are cached briefly because programs poll; revoking a
/// token clears its entry, and a disabled owner stops working once the entry expires.
/// </summary>
public sealed class ApiTokenValidator(ArgusDbContext db, IMemoryCache cache, TimeProvider time)
{
    private static readonly TimeSpan KnownTokenLifetime = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan UnknownTokenLifetime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LastUsedResolution = TimeSpan.FromMinutes(5);

    public async Task<ApiTokenIdentity?> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        if (!SecretTokens.LooksValid(token, ApiTokenDefaults.Prefix))
        {
            return null;
        }

        var hash = SecretTokens.Hash(token);
        if (cache.TryGetValue(CacheKey(hash), out CachedLookup? cached) && cached is not null)
        {
            return cached.Identity;
        }

        var found = await db.ApiTokens
            .AsNoTracking()
            .Where(t => t.TokenHash == hash && t.RevokedAt == null && t.Owner!.DisabledAt == null)
            .Select(t => new { t.Id, t.OwnerId, t.Owner!.UserName, t.LastUsedAt })
            .FirstOrDefaultAsync(cancellationToken);

        ApiTokenIdentity? identity = null;
        if (found is not null)
        {
            var roles = await db.UserRoles
                .Where(userRole => userRole.UserId == found.OwnerId)
                .Join(db.Roles, userRole => userRole.RoleId, role => role.Id, (_, role) => role.Name!)
                .ToListAsync(cancellationToken);
            identity = new ApiTokenIdentity(found.Id, found.OwnerId, found.UserName ?? "", roles);

            var now = time.GetUtcNow();
            if (found.LastUsedAt is null || now - found.LastUsedAt > LastUsedResolution)
            {
                await db.ApiTokens.Where(t => t.Id == found.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(t => t.LastUsedAt, now), cancellationToken);
            }
        }

        cache.Set(CacheKey(hash), new CachedLookup(identity), identity is null ? UnknownTokenLifetime : KnownTokenLifetime);
        return identity;
    }

    /// <summary>Forgets a token at once, e.g. when it is revoked.</summary>
    public void Invalidate(string tokenHash) => cache.Remove(CacheKey(tokenHash));

    private static string CacheKey(string tokenHash) => "api-token:" + tokenHash;

    private sealed record CachedLookup(ApiTokenIdentity? Identity);
}

/// <summary>Authenticates programs presenting <c>Authorization: Bearer argus_at_…</c> as the token's owner.</summary>
public sealed class ApiTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiTokenValidator validator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!ApiTokenDefaults.CarriesApiToken(Request))
        {
            return AuthenticateResult.NoResult();
        }

        var token = Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim();
        var identity = await validator.ValidateAsync(token, Context.RequestAborted);
        if (identity is null)
        {
            return AuthenticateResult.Fail("Invalid API token.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, identity.UserId.ToString()),
            new(ClaimTypes.Name, identity.UserName),
            new(ApiTokenDefaults.TokenIdClaim, identity.TokenId.ToString()),
        };
        claims.AddRange(identity.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }
}

public static class ApiTokenServiceExtensions
{
    /// <summary>Call after AddArgusAuth: it replaces the default scheme Identity sets.</summary>
    public static IServiceCollection AddArgusApiTokens(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddScoped<ApiTokenValidator>();

        services.AddAuthentication(options =>
            {
                // Requests that carry an API token are judged by it alone; everything else by the session cookie.
                options.DefaultAuthenticateScheme = ApiTokenDefaults.SelectorScheme;
                options.DefaultChallengeScheme = ApiTokenDefaults.SelectorScheme;
                options.DefaultForbidScheme = ApiTokenDefaults.SelectorScheme;
            })
            .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(ApiTokenDefaults.Scheme, configureOptions: null)
            .AddPolicyScheme(ApiTokenDefaults.SelectorScheme, displayName: null, options =>
                options.ForwardDefaultSelector = context => ApiTokenDefaults.CarriesApiToken(context.Request)
                    ? ApiTokenDefaults.Scheme
                    : IdentityConstants.ApplicationScheme);

        return services;
    }

    /// <summary>
    /// API tokens only read: any other request made with one is refused, whatever the endpoint. Runs
    /// after authentication, so it sees who the request is from.
    /// </summary>
    public static IApplicationBuilder UseApiTokensReadOnly(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var method = context.Request.Method;
            var reads = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
            if (!reads && context.User.HasClaim(claim => claim.Type == ApiTokenDefaults.TokenIdClaim))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context,
                    ProblemDetails =
                    {
                        Status = StatusCodes.Status403Forbidden,
                        Title = "API tokens are read-only",
                        Detail = "Sign in to Argus to make changes.",
                    },
                });
                return;
            }

            await next(context);
        });
}
