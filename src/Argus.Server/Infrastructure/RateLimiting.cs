using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace Argus.Server.Infrastructure;

public sealed class RateLimitOptions
{
    public const string SectionName = "Argus:RateLimits";

    /// <summary>Sign-in, setup, registration and password-change attempts allowed per client IP per minute.</summary>
    [Range(1, 100_000)]
    public int AuthPermitsPerMinute { get; set; } = 10;

    /// <summary>Agent registrations allowed per client IP per minute (a fleet may share one NAT address).</summary>
    [Range(1, 100_000)]
    public int AgentRegisterPermitsPerMinute { get; set; } = 60;

    /// <summary>Metric batches allowed per agent per minute (agents send one every collection interval).</summary>
    [Range(1, 100_000)]
    public int AgentIngestPermitsPerMinute { get; set; } = 120;

    /// <summary>Test notifications each person may send per minute (they can reach any address).</summary>
    [Range(1, 100_000)]
    public int NotificationTestPermitsPerMinute { get; set; } = 5;
}

public static class RateLimiting
{
    /// <summary>Policy for endpoints that check passwords.</summary>
    public const string AuthPolicy = "auth";

    /// <summary>Policy for agents exchanging enrollment tokens.</summary>
    public const string AgentRegisterPolicy = "agent-register";

    /// <summary>Policy for agents posting metrics.</summary>
    public const string AgentIngestPolicy = "agent-ingest";

    /// <summary>Policy for sending test notifications.</summary>
    public const string NotificationTestPolicy = "notification-test";

    public static IServiceCollection AddArgusRateLimiting(this IServiceCollection services)
    {
        services.AddValidatedOptions<RateLimitOptions>(RateLimitOptions.SectionName);

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };

            limiter.AddPolicy(AuthPolicy, context =>
                PerMinute(context, ClientAddress(context), options => options.AuthPermitsPerMinute));
            limiter.AddPolicy(AgentRegisterPolicy, context =>
                PerMinute(context, ClientAddress(context), options => options.AgentRegisterPermitsPerMinute));

            // Agent authentication runs after the rate limiter, so agents are told apart by (a hash of) their key.
            limiter.AddPolicy(AgentIngestPolicy, context =>
                PerMinute(context, SecretTokens.Hash(context.Request.Headers.Authorization.ToString()),
                    options => options.AgentIngestPermitsPerMinute));

            // Authentication runs before the rate limiter, so people are told apart by their user id.
            limiter.AddPolicy(NotificationTestPolicy, context =>
                PerMinute(context, context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ClientAddress(context),
                    options => options.NotificationTestPermitsPerMinute));
        });

        return services;
    }

    private static string ClientAddress(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static RateLimitPartition<string> PerMinute(HttpContext context, string partitionKey, Func<RateLimitOptions, int> permits)
    {
        var options = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits(options),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
    }
}
