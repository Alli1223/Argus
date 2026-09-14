using System.ComponentModel.DataAnnotations;
using System.Globalization;
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
}

public static class RateLimiting
{
    /// <summary>Policy for endpoints that check passwords.</summary>
    public const string AuthPolicy = "auth";

    /// <summary>Policy for agents exchanging enrollment tokens.</summary>
    public const string AgentRegisterPolicy = "agent-register";

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
                PerClientPerMinute(context, options => options.AuthPermitsPerMinute));
            limiter.AddPolicy(AgentRegisterPolicy, context =>
                PerClientPerMinute(context, options => options.AgentRegisterPermitsPerMinute));
        });

        return services;
    }

    private static RateLimitPartition<string> PerClientPerMinute(HttpContext context, Func<RateLimitOptions, int> permits)
    {
        var options = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
        return RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits(options),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
    }
}
