using System.Security.Claims;
using Argus.Server.Data;
using Argus.Server.Features.Agents;
using Argus.Server.Features.Auth;
using Argus.Server.Features.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Argus.Server.Features.Users;

/// <summary>User administration, available to administrators only.</summary>
public static class UserEndpoints
{
    /// <summary>Serialises changes that could remove the last active administrator.</summary>
    private const long AdminChangeLockKey = 0x4152_4755_5302;

    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder routes)
    {
        var users = routes.MapGroup("/users")
            .WithTags("Users")
            .RequireAuthorization(policy => policy.RequireRole(Roles.Admin));

        users.MapGet("/", ListAsync);
        users.MapPost("/", CreateAsync);
        users.MapPut("/{id:guid}", UpdateAsync);
        users.MapPost("/{id:guid}/disable", DisableAsync);
        users.MapPost("/{id:guid}/enable", EnableAsync);
        users.MapPost("/{id:guid}/reset-password", ResetPasswordAsync);
        users.MapDelete("/{id:guid}", DeleteAsync);

        return routes;
    }

    private static async Task<List<UserSummary>> ListAsync(ArgusDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var rows = await db.Users
            .OrderBy(user => user.Email)
            .Select(user => new
            {
                User = user,
                Role = db.UserRoles
                    .Where(link => link.UserId == user.Id)
                    .Join(db.Roles, link => link.RoleId, role => role.Id, (_, role) => role.Name)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var now = time.GetUtcNow();
        return rows.Select(row => ToSummary(row.User, row.Role ?? Roles.User, now)).ToList();
    }

    private static async Task<Results<Created<UserSummary>, ValidationProblem>> CreateAsync(
        CreateUserRequest request, ArgusDbContext db, UserManager<ArgusUser> users, TimeProvider time, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var account = new NewAccountRequest { Email = request.Email, DisplayName = request.DisplayName, Password = request.Password };
        var (user, result) = await UserAccounts.CreateAsync(db, users, account, request.Role, time);
        if (user is null)
        {
            return UserAccounts.IdentityValidationProblem(result);
        }

        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Created($"/api/users/{user.Id}", ToSummary(user, request.Role, time.GetUtcNow()));
    }

    private static async Task<Results<Ok<UserSummary>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        Guid id, UpdateUserRequest request, ArgusDbContext db, UserManager<ArgusUser> users, TimeProvider time, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return TypedResults.NotFound();
        }

        await using var transaction = await BeginAdminChangeAsync(db, cancellationToken);
        var currentRoles = await users.GetRolesAsync(user);
        if (currentRoles.Contains(Roles.Admin) && request.Role != Roles.Admin && await IsLastActiveAdminAsync(db, user.Id, cancellationToken))
        {
            return LastAdminProblem();
        }

        user.DisplayName = request.DisplayName.Trim();
        var result = await users.UpdateAsync(user);
        if (result.Succeeded && !currentRoles.SequenceEqual([request.Role]))
        {
            result = await users.RemoveFromRolesAsync(user, currentRoles);
            if (result.Succeeded)
            {
                result = await users.AddToRoleAsync(user, request.Role);
            }

            // Roles are baked into the session cookie; rotating the stamp makes the user sign in again.
            if (result.Succeeded)
            {
                result = await users.UpdateSecurityStampAsync(user);
            }
        }

        if (!result.Succeeded)
        {
            return UserAccounts.IdentityValidationProblem(result);
        }

        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Ok(ToSummary(user, request.Role, time.GetUtcNow()));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DisableAsync(
        Guid id, ClaimsPrincipal principal, ArgusDbContext db, UserManager<ArgusUser> users, TimeProvider time, CancellationToken cancellationToken)
    {
        if (IsSelf(principal, users, id))
        {
            return SelfProblem("disable");
        }

        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return TypedResults.NotFound();
        }

        await using var transaction = await BeginAdminChangeAsync(db, cancellationToken);
        if (await users.IsInRoleAsync(user, Roles.Admin) && await IsLastActiveAdminAsync(db, user.Id, cancellationToken))
        {
            return LastAdminProblem();
        }

        user.DisabledAt ??= time.GetUtcNow();
        await users.UpdateAsync(user);

        // Invalidates the user's open sessions at their next validation (within a minute).
        await users.UpdateSecurityStampAsync(user);

        await transaction.CommitAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> EnableAsync(Guid id, UserManager<ArgusUser> users)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return TypedResults.NotFound();
        }

        user.DisabledAt = null;
        await users.UpdateAsync(user);
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> ResetPasswordAsync(
        Guid id, ResetPasswordRequest request, UserManager<ArgusUser> users)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return TypedResults.NotFound();
        }

        // Resetting also rotates the security stamp, signing the user out everywhere.
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded)
        {
            return UserAccounts.IdentityValidationProblem(result, passwordField: "newPassword");
        }

        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteAsync(
        Guid id,
        ClaimsPrincipal principal,
        ArgusDbContext db,
        UserManager<ArgusUser> users,
        TimeSeriesQueries series,
        AgentKeyValidator agentKeys,
        CancellationToken cancellationToken)
    {
        if (IsSelf(principal, users, id))
        {
            return SelfProblem("delete");
        }

        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return TypedResults.NotFound();
        }

        await using var transaction = await BeginAdminChangeAsync(db, cancellationToken);
        if (await users.IsInRoleAsync(user, Roles.Admin) && await IsLastActiveAdminAsync(db, user.Id, cancellationToken))
        {
            return LastAdminProblem();
        }

        // Their hosts, rules, tokens and alerts go with the account (cascading deletes). Samples live in
        // hypertables without foreign keys, so they, and the agents' cached keys, are cleared here.
        var hosts = await db.Hosts
            .Where(host => host.OwnerId == user.Id)
            .Select(host => new { host.Id, host.AgentKeyHash })
            .ToListAsync(cancellationToken);

        await users.DeleteAsync(user);
        await transaction.CommitAsync(cancellationToken);

        foreach (var host in hosts)
        {
            agentKeys.Invalidate(host.AgentKeyHash);
            await series.DeleteHostDataAsync(host.Id, cancellationToken);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginAdminChangeAsync(
        ArgusDbContext db, CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AdminChangeLockKey})", cancellationToken);
        return transaction;
    }

    /// <summary>True when no other enabled administrator exists besides <paramref name="userId"/>.</summary>
    private static Task<bool> IsLastActiveAdminAsync(ArgusDbContext db, Guid userId, CancellationToken cancellationToken) =>
        db.UserRoles
            .Where(link => link.RoleId == Roles.AdminId && link.UserId != userId)
            .Join(db.Users, link => link.UserId, user => user.Id, (_, user) => user)
            .AllAsync(user => user.DisabledAt != null, cancellationToken);

    private static bool IsSelf(ClaimsPrincipal principal, UserManager<ArgusUser> users, Guid id) =>
        Guid.TryParse(users.GetUserId(principal), out var currentId) && currentId == id;

    private static UserSummary ToSummary(ArgusUser user, string role, DateTimeOffset now) =>
        new(user.Id, user.Email ?? "", user.DisplayName, role, user.CreatedAt, user.LastLoginAt,
            IsDisabled: user.DisabledAt is not null,
            IsLockedOut: user.LockoutEnd > now);

    private static ProblemHttpResult LastAdminProblem() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Last administrator",
            detail: "At least one active administrator must remain.");

    private static ProblemHttpResult SelfProblem(string action) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Not allowed on your own account",
            detail: $"You cannot {action} your own account.");
}
