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
}

public static class RateLimiting
{
    /// <summary>Policy for endpoints that check passwords.</summary>
    public const string AuthPolicy = "auth";

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
            {
                var options = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.AuthPermitsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    });
            });
        });

        return services;
    }
}
