using Microsoft.Extensions.FileProviders;

namespace Argus.Server.Infrastructure;

/// <summary>
/// Serves the built web app (web/dist, copied to wwwroot in the container image) and answers every
/// page URL with its index.html, so that reloading a page the app routed to still works. Without a
/// build (as in development, where Vite serves the app) nothing is added.
/// </summary>
public static class WebAppHosting
{
    /// <summary>Folder with the built app; relative paths are resolved against the content root.</summary>
    public const string RootSetting = "Argus:WebRoot";

    // Vite puts a content hash in the name of everything under /assets, so those files never change.
    private const string ForeverCache = "public, max-age=31536000, immutable";

    /// <summary>Paths the server answers itself: unknown URLs there are 404s, never the app.</summary>
    private static readonly string[] ServerPrefixes = ["/api", "/hubs", "/health", "/downloads", "/openapi", "/scalar"];

    public static WebApplication UseArgusWebApp(this WebApplication app)
    {
        var root = Path.GetFullPath(app.Configuration[RootSetting] ?? "wwwroot", app.Environment.ContentRootPath);
        if (!File.Exists(Path.Combine(root, "index.html")))
        {
            return app;
        }

        var files = new PhysicalFileProvider(root);
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = files,
            OnPrepareResponse = context => context.Context.Response.Headers.CacheControl =
                context.Context.Request.Path.StartsWithSegments("/assets") ? ForeverCache : "no-cache",
        });

        var index = files.GetFileInfo("index.html");

        // The default pattern leaves file-like paths alone. That matters: routing runs first in minimal
        // hosting, and the static file middlewares (these and /downloads) only serve requests that
        // matched no endpoint.
        app.MapFallback(async context =>
        {
            var request = context.Request;
            var read = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method);
            if (!read || IsServerPath(request.Path))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength = index.Length;
            context.Response.Headers.CacheControl = "no-cache";
            if (HttpMethods.IsGet(request.Method))
            {
                await context.Response.SendFileAsync(index, context.RequestAborted);
            }
        }).AllowAnonymous();

        return app;
    }

    private static bool IsServerPath(PathString path) => ServerPrefixes.Any(prefix => path.StartsWithSegments(prefix));
}
