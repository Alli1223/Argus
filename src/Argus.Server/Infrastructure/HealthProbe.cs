namespace Argus.Server.Infrastructure;

/// <summary>
/// <c>Argus.Server health-check [url]</c> asks a running server whether it is ready and exits with 0
/// if it is. Container health checks use it: the runtime image has no shell and no curl.
/// </summary>
internal static class HealthProbe
{
    public const string Command = "health-check";

    public static async Task<int> RunAsync(string[] args)
    {
        var url = args.Length > 1 ? args[1] : ProbeUrl(Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS"));
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            using var response = await client.GetAsync(url);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
        {
            await Console.Error.WriteLineAsync($"Health check failed: {exception.Message}");
            return 1;
        }
    }

    /// <summary>The readiness endpoint on the first port the server listens on (8080 in the container image).</summary>
    internal static string ProbeUrl(string? httpPorts)
    {
        var port = httpPorts?
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return $"http://localhost:{(string.IsNullOrEmpty(port) ? "8080" : port)}/health/ready";
    }
}
