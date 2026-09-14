using System.Security.Claims;
using System.Text.Encodings.Web;
using Argus.Contracts.Agent;
using Argus.Server.Data;
using Argus.Server.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Argus.Server.Features.Agents;

public static class AgentKeyDefaults
{
    public const string Scheme = "AgentKey";
    public const string Policy = "Agent";
    public const string HostIdClaim = "argus:host_id";
    public const string OwnerIdClaim = "argus:owner_id";

    public static Guid GetHostId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(HostIdClaim) ?? throw new InvalidOperationException("Not an agent principal."));
}

public sealed record AgentIdentity(Guid HostId, Guid OwnerId);

/// <summary>
/// Resolves agent keys to hosts. Lookups are cached briefly because agents report every few seconds;
/// unknown keys are cached too so a misconfigured agent cannot hammer the database.
/// </summary>
public sealed class AgentKeyValidator(ArgusDbContext db, IMemoryCache cache)
{
    private static readonly TimeSpan KnownKeyLifetime = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan UnknownKeyLifetime = TimeSpan.FromSeconds(30);

    public async Task<AgentIdentity?> ValidateAsync(string key, CancellationToken cancellationToken)
    {
        if (!SecretTokens.LooksValid(key, AgentApi.AgentKeyPrefix))
        {
            return null;
        }

        var hash = SecretTokens.Hash(key);
        if (cache.TryGetValue(CacheKey(hash), out CachedLookup? cached) && cached is not null)
        {
            return cached.Identity;
        }

        var identity = await db.Hosts
            .AsNoTracking()
            .Where(host => host.AgentKeyHash == hash)
            .Select(host => new AgentIdentity(host.Id, host.OwnerId))
            .FirstOrDefaultAsync(cancellationToken);

        cache.Set(CacheKey(hash), new CachedLookup(identity), identity is null ? UnknownKeyLifetime : KnownKeyLifetime);
        return identity;
    }

    /// <summary>Forgets a key at once, e.g. when its host is deleted or re-registered.</summary>
    public void Invalidate(string keyHash) => cache.Remove(CacheKey(keyHash));

    private static string CacheKey(string keyHash) => "agent-key:" + keyHash;

    private sealed record CachedLookup(AgentIdentity? Identity);
}

/// <summary>Authenticates agents presenting <c>Authorization: Bearer argus_ak_…</c>.</summary>
public sealed class AgentKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AgentKeyValidator validator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var identity = await validator.ValidateAsync(header[BearerPrefix.Length..].Trim(), Context.RequestAborted);
        if (identity is null)
        {
            return AuthenticateResult.Fail("Invalid agent key.");
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, identity.HostId.ToString()),
            new Claim(AgentKeyDefaults.HostIdClaim, identity.HostId.ToString()),
            new Claim(AgentKeyDefaults.OwnerIdClaim, identity.OwnerId.ToString()),
        };
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

public static class AgentServiceExtensions
{
    public static IServiceCollection AddArgusAgents(this IServiceCollection services)
    {
        services.AddValidatedOptions<AgentOptions>(AgentOptions.SectionName);
        services.AddMemoryCache();
        services.AddScoped<AgentKeyValidator>();

        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, AgentKeyAuthenticationHandler>(AgentKeyDefaults.Scheme, configureOptions: null);

        // Only agent keys are accepted on agent endpoints; a browser session never is.
        services.AddAuthorizationBuilder()
            .AddPolicy(AgentKeyDefaults.Policy, policy => policy
                .AddAuthenticationSchemes(AgentKeyDefaults.Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(AgentKeyDefaults.HostIdClaim));

        return services;
    }
}
