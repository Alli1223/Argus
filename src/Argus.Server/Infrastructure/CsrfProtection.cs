namespace Argus.Server.Infrastructure;

/// <summary>
/// Cross-site request forgery defence for the cookie-authenticated API: state-changing requests must
/// carry a custom header. Browsers only let another origin add custom headers after a CORS preflight,
/// which this server never approves, so a forged cross-site request cannot include it.
/// </summary>
public static class CsrfProtection
{
    public const string HeaderName = "X-Argus-Csrf";

    public static IApplicationBuilder UseCsrfProtection(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (RequiresHeader(context.Request) && !context.Request.Headers.ContainsKey(HeaderName))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context,
                    ProblemDetails =
                    {
                        Status = StatusCodes.Status403Forbidden,
                        Title = "Missing anti-forgery header",
                        Detail = $"State-changing requests must include the '{HeaderName}' header.",
                    },
                });
                return;
            }

            await next(context);
        });

    private static bool RequiresHeader(HttpRequest request) =>
        !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method) || HttpMethods.IsTrace(request.Method))
        && request.Path.StartsWithSegments("/api")
        // Agents authenticate with bearer keys, which browsers never attach on their own.
        && !request.Path.StartsWithSegments("/api/agent");
}
