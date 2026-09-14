using System.Security.Claims;
using Argus.Contracts.Agent;
using Argus.Server.Data;
using Argus.Server.Features.Enrollment;
using Argus.Server.Features.Hosts;
using Argus.Server.Features.Metrics;
using Argus.Server.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Argus.Server.Features.Agents;

/// <summary>The API agents talk to. Routes come from the shared contracts so both sides agree.</summary>
public static class AgentEndpoints
{
    /// <summary>Largest (decompressed) metrics batch accepted.</summary>
    private const long MaxMetricsBodyBytes = 8 * 1024 * 1024;

    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost(AgentApi.Register, RegisterAsync)
            .WithTags("Agent")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimiting.AgentRegisterPolicy);

        routes.MapPut(AgentApi.Inventory, UpdateInventoryAsync)
            .WithTags("Agent")
            .RequireAuthorization(AgentKeyDefaults.Policy);

        routes.MapPost(AgentApi.Metrics, IngestMetricsAsync)
            .WithTags("Agent")
            .RequireAuthorization(AgentKeyDefaults.Policy)
            .RequireRateLimiting(RateLimiting.AgentIngestPolicy)
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MaxMetricsBodyBytes));

        return routes;
    }

    private static async Task<Results<Ok<RegisterAgentResponse>, ValidationProblem, ProblemHttpResult>> RegisterAsync(
        RegisterAgentRequest request,
        ArgusDbContext db,
        AgentKeyValidator keys,
        IOptions<AgentOptions> options,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        if (ValidateRegistration(request) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (!SecretTokens.LooksValid(request.EnrollmentToken, AgentApi.EnrollmentTokenPrefix))
        {
            return InvalidEnrollmentToken();
        }

        var now = time.GetUtcNow();
        var tokenHash = SecretTokens.Hash(request.EnrollmentToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Lock the token row so concurrent registrations cannot exceed its use limit.
        var token = (await db.EnrollmentTokens
                .FromSql($"SELECT * FROM enrollment_tokens WHERE token_hash = {tokenHash} FOR UPDATE")
                .ToListAsync(cancellationToken))
            .SingleOrDefault();
        if (token is null || !token.IsUsable(now))
        {
            return InvalidEnrollmentToken();
        }

        var host = await db.Hosts.SingleOrDefaultAsync(
            h => h.OwnerId == token.OwnerId && h.MachineId == request.MachineId, cancellationToken);
        if (host is null)
        {
            host = new MonitoredHost
            {
                OwnerId = token.OwnerId,
                MachineId = request.MachineId,
                CreatedAt = now,
                Tags = [.. token.Tags],
            };
            db.Hosts.Add(host);
        }
        else
        {
            // The same machine enrolling again (e.g. after a reinstall) keeps its history but gets a new key.
            keys.Invalidate(host.AgentKeyHash);
            host.Tags = host.Tags.Union(token.Tags).ToList();
        }

        host.ApplyInventory(request.SystemInfo, request.AgentVersion, now);
        if (string.IsNullOrWhiteSpace(host.DisplayName))
        {
            host.DisplayName = host.Hostname;
        }

        host.LastSeenAt = now;

        var agentKey = SecretTokens.Generate(AgentApi.AgentKeyPrefix);
        host.AgentKeyHash = SecretTokens.Hash(agentKey);

        token.UseCount++;
        token.LastUsedAt = now;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        loggers.CreateLogger(typeof(AgentEndpoints).FullName!).LogInformation(
            "Host {HostId} ({Hostname}) registered with enrollment token {TokenId}", host.Id, host.Hostname, token.Id);

        return TypedResults.Ok(new RegisterAgentResponse
        {
            HostId = host.Id,
            AgentKey = agentKey,
            Settings = options.Value.ToAgentSettings(),
        });
    }

    private static async Task<Results<Ok<AgentSettings>, NotFound>> UpdateInventoryAsync(
        InventoryReport report,
        ClaimsPrincipal principal,
        ArgusDbContext db,
        IOptions<AgentOptions> options,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var host = await db.Hosts.FindAsync([principal.GetHostId()], cancellationToken);
        if (host is null)
        {
            return TypedResults.NotFound();
        }

        var now = time.GetUtcNow();
        host.ApplyInventory(report.SystemInfo, report.AgentVersion, now);
        host.LastSeenAt = now;
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(options.Value.ToAgentSettings());
    }

    private static async Task<Results<Ok<MetricsBatchResponse>, ProblemHttpResult>> IngestMetricsAsync(
        MetricsBatch batch,
        ClaimsPrincipal principal,
        MetricsIngestor ingestor,
        IOptions<IngestOptions> ingestOptions,
        IOptions<AgentOptions> agentOptions,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        if (MetricsBatchValidator.ValidateShape(batch) is { } problem)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid metrics batch", detail: problem);
        }

        var now = time.GetUtcNow();
        var hostId = principal.GetHostId();
        var samples = MetricsBatchValidator.Sanitize(
            batch.Samples,
            now,
            maxAge: TimeSpan.FromHours(ingestOptions.Value.MaxSampleAgeHours),
            maxClockSkew: TimeSpan.FromSeconds(ingestOptions.Value.MaxClockSkewSeconds),
            out var dropped);

        if (dropped > 0)
        {
            loggers.CreateLogger(typeof(AgentEndpoints).FullName!).LogWarning(
                "Dropped {Count} sample(s) from host {HostId} with timestamps outside the accepted window; check the host's clock",
                dropped, hostId);
        }

        var accepted = await ingestor.IngestAsync(hostId, samples, now, cancellationToken);
        return TypedResults.Ok(new MetricsBatchResponse { Accepted = accepted, Settings = agentOptions.Value.ToAgentSettings() });
    }

    private static Dictionary<string, string[]>? ValidateRegistration(RegisterAgentRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.MachineId) || request.MachineId.Length > 128)
        {
            errors["machineId"] = ["The machine id must be 1-128 characters."];
        }

        if (string.IsNullOrWhiteSpace(request.AgentVersion))
        {
            errors["agentVersion"] = ["The agent version is required."];
        }

        if (string.IsNullOrWhiteSpace(request.SystemInfo.Hostname))
        {
            errors["systemInfo.hostname"] = ["The hostname is required."];
        }

        return errors.Count == 0 ? null : errors;
    }

    private static ProblemHttpResult InvalidEnrollmentToken() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Invalid enrollment token",
            detail: "The enrollment token is unknown, expired, revoked or used up.");
}
