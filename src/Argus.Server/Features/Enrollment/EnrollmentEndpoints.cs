using System.Security.Claims;
using Argus.Contracts.Agent;
using Argus.Server.Data;
using Argus.Server.Features.Auth;
using Argus.Server.Features.Hosts;
using Argus.Server.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Argus.Server.Features.Enrollment;

/// <summary>Enrollment tokens belong to the user who created them and register hosts on their behalf.</summary>
public static class EnrollmentEndpoints
{
    public static IEndpointRouteBuilder MapEnrollmentEndpoints(this IEndpointRouteBuilder routes)
    {
        var tokens = routes.MapGroup("/enrollment-tokens").WithTags("Enrollment");

        tokens.MapGet("/", ListAsync);
        tokens.MapPost("/", CreateAsync);
        tokens.MapDelete("/{id:guid}", RevokeAsync);

        return routes;
    }

    private static async Task<List<EnrollmentTokenSummary>> ListAsync(
        ClaimsPrincipal principal, ArgusDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var ownerId = principal.GetUserId();
        var tokens = await db.EnrollmentTokens
            .AsNoTracking()
            .Where(token => token.OwnerId == ownerId)
            .OrderByDescending(token => token.CreatedAt)
            .ToListAsync(cancellationToken);

        var now = time.GetUtcNow();
        return tokens.Select(token => ToSummary(token, now)).ToList();
    }

    private static async Task<Results<Created<CreatedEnrollmentToken>, ValidationProblem>> CreateAsync(
        CreateEnrollmentTokenRequest request,
        ClaimsPrincipal principal,
        ArgusDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!HostTags.TryNormalize(request.Tags, out var tags, out var error))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["tags"] = [error] });
        }

        var now = time.GetUtcNow();
        var secret = SecretTokens.Generate(AgentApi.EnrollmentTokenPrefix);
        var token = new EnrollmentToken
        {
            OwnerId = principal.GetUserId(),
            Name = request.Name.Trim(),
            TokenHash = SecretTokens.Hash(secret),
            TokenPrefix = SecretTokens.DisplayPrefix(secret, AgentApi.EnrollmentTokenPrefix),
            Tags = tags,
            CreatedAt = now,
            ExpiresAt = request.ExpiresInHours is { } hours ? now.AddHours(hours) : null,
            MaxUses = request.MaxUses,
        };

        db.EnrollmentTokens.Add(token);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/enrollment-tokens/{token.Id}", new CreatedEnrollmentToken(secret, ToSummary(token, now)));
    }

    private static async Task<Results<NoContent, NotFound>> RevokeAsync(
        Guid id, ClaimsPrincipal principal, ArgusDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var ownerId = principal.GetUserId();
        var token = await db.EnrollmentTokens.SingleOrDefaultAsync(t => t.Id == id && t.OwnerId == ownerId, cancellationToken);
        if (token is null)
        {
            return TypedResults.NotFound();
        }

        token.RevokedAt ??= time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static EnrollmentTokenSummary ToSummary(EnrollmentToken token, DateTimeOffset now) =>
        new(token.Id, token.Name, token.TokenPrefix, token.Tags, token.CreatedAt, token.ExpiresAt, token.MaxUses,
            token.UseCount, token.LastUsedAt, token.RevokedAt, IsActive: token.IsUsable(now));
}
