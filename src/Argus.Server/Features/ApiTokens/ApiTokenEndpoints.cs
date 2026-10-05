using System.Security.Claims;
using Argus.Server.Data;
using Argus.Server.Features.Auth;
using Argus.Server.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Argus.Server.Features.ApiTokens;

/// <summary>A user's own API tokens. Managing them takes a browser session; a token cannot make more.</summary>
public static class ApiTokenEndpoints
{
    public static IEndpointRouteBuilder MapApiTokenEndpoints(this IEndpointRouteBuilder routes)
    {
        var tokens = routes.MapGroup("/account/api-tokens").WithTags("Account");

        tokens.MapGet("/", ListAsync);
        tokens.MapPost("/", CreateAsync);
        tokens.MapDelete("/{id:guid}", RevokeAsync);

        return routes;
    }

    private static async Task<List<ApiTokenSummary>> ListAsync(
        ClaimsPrincipal principal, ArgusDbContext db, CancellationToken cancellationToken)
    {
        var ownerId = principal.GetUserId();
        var tokens = await db.ApiTokens
            .AsNoTracking()
            .Where(token => token.OwnerId == ownerId)
            .OrderByDescending(token => token.CreatedAt)
            .ToListAsync(cancellationToken);

        return tokens.Select(ToSummary).ToList();
    }

    private static async Task<Created<CreatedApiToken>> CreateAsync(
        CreateApiTokenRequest request,
        ClaimsPrincipal principal,
        ArgusDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var secret = SecretTokens.Generate(ApiTokenDefaults.Prefix);
        var token = new ApiToken
        {
            OwnerId = principal.GetUserId(),
            Name = request.Name.Trim(),
            TokenHash = SecretTokens.Hash(secret),
            TokenPrefix = SecretTokens.DisplayPrefix(secret, ApiTokenDefaults.Prefix),
            CreatedAt = time.GetUtcNow(),
        };

        db.ApiTokens.Add(token);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/account/api-tokens/{token.Id}", new CreatedApiToken(secret, ToSummary(token)));
    }

    private static async Task<Results<NoContent, NotFound>> RevokeAsync(
        Guid id,
        ClaimsPrincipal principal,
        ArgusDbContext db,
        ApiTokenValidator validator,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var ownerId = principal.GetUserId();
        var token = await db.ApiTokens.SingleOrDefaultAsync(t => t.Id == id && t.OwnerId == ownerId, cancellationToken);
        if (token is null)
        {
            return TypedResults.NotFound();
        }

        token.RevokedAt ??= time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        // Stop accepting it at once rather than when its cache entry expires.
        validator.Invalidate(token.TokenHash);
        return TypedResults.NoContent();
    }

    private static ApiTokenSummary ToSummary(ApiToken token) =>
        new(token.Id, token.Name, token.TokenPrefix, token.CreatedAt, token.LastUsedAt, token.RevokedAt,
            IsActive: token.RevokedAt is null);
}
