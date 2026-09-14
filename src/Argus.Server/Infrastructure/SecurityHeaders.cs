namespace Argus.Server.Infrastructure;

public static class SecurityHeaders
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'; " +
        "img-src 'self' data:; font-src 'self' data:; style-src 'self' 'unsafe-inline'; connect-src 'self'";

    /// <summary>
    /// Adds browser hardening headers to every response. They are applied when the response starts so
    /// that error responses (which clear headers) get them too.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";

                // The interactive API reference (Development only) brings its own scripts and styles.
                if (!IsApiReference(context.Request.Path))
                {
                    headers.ContentSecurityPolicy = ContentSecurityPolicy;
                }

                return Task.CompletedTask;
            });

            await next(context);
        });

    private static bool IsApiReference(PathString path) =>
        path.StartsWithSegments("/scalar") || path.StartsWithSegments("/openapi");
}
