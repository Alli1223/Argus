using System.ComponentModel.DataAnnotations;
using Argus.Server.Infrastructure;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace Argus.Server.Features.Downloads;

public sealed class DownloadsOptions
{
    public const string SectionName = "Argus:Downloads";

    /// <summary>
    /// Folder with agent builds, one sub-folder per runtime (<c>linux-x64/argus-agent</c>,
    /// <c>win-x64/argus-agent.exe</c>). Relative paths are resolved against the content root.
    /// </summary>
    [Required]
    public string AgentDirectory { get; set; } = "agent-dist";
}

/// <summary>
/// Install scripts and agent builds. Both are public: they contain no secrets, and the machine being
/// installed has no session. The enrollment token is what authorises an agent.
/// </summary>
public static class DownloadEndpoints
{
    private static readonly string[] Runtimes = ["linux-x64", "linux-arm64", "win-x64"];

    public static IServiceCollection AddArgusDownloads(this IServiceCollection services)
    {
        services.AddValidatedOptions<DownloadsOptions>(DownloadsOptions.SectionName);
        return services;
    }

    /// <summary>Serves the install scripts that ship with the server (copied from deploy/agent at build time).</summary>
    public static IApplicationBuilder UseArgusDownloads(this IApplicationBuilder app)
    {
        var scripts = Path.Combine(AppContext.BaseDirectory, "downloads");
        if (!Directory.Exists(scripts))
        {
            return app;
        }

        var types = new FileExtensionContentTypeProvider();
        types.Mappings[".sh"] = "text/x-shellscript";
        types.Mappings[".ps1"] = "text/plain";
        types.Mappings[".service"] = "text/plain";

        return app.UseStaticFiles(new StaticFileOptions
        {
            RequestPath = "/downloads",
            FileProvider = new PhysicalFileProvider(scripts),
            ContentTypeProvider = types,
            OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
        });
    }

    public static IEndpointRouteBuilder MapAgentDownloads(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/downloads/agent/{runtime}/{file}", GetAgent)
            .WithTags("Downloads")
            .AllowAnonymous();
        return routes;
    }

    private static IResult GetAgent(
        string runtime, string file, IOptions<DownloadsOptions> options, IWebHostEnvironment environment)
    {
        var expected = runtime.StartsWith("win-", StringComparison.Ordinal) ? "argus-agent.exe" : "argus-agent";
        if (!Runtimes.Contains(runtime) || file != expected)
        {
            return TypedResults.NotFound();
        }

        var directory = Path.GetFullPath(options.Value.AgentDirectory, environment.ContentRootPath);
        var path = Path.Combine(directory, runtime, file);
        return File.Exists(path)
            ? TypedResults.PhysicalFile(path, "application/octet-stream", file, enableRangeProcessing: true)
            : TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Agent build not available",
                detail: $"This server has no {runtime} agent build. Build one with build/package-agent.sh " +
                    "and point Argus:Downloads:AgentDirectory at its output.");
    }
}
