using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Argus.Agent.Configuration;
using Argus.Agent.Updates;
using Argus.Contracts.Agent;

namespace Argus.Agent.Transport;

/// <summary>Talks to the server's agent API and classifies failures so callers know whether to retry.</summary>
internal sealed class ArgusClient(HttpClient http)
{
    /// <summary>Bodies larger than this are gzip-compressed.</summary>
    private const int CompressionThreshold = 1024;

    public static ArgusClient Create(AgentConfig config)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),

            // Recycle connections now and then so DNS changes (e.g. a moved server) are picked up.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };

        var http = new HttpClient(handler) { BaseAddress = config.ServerUri, Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(AgentInfo.UserAgent);
        return new ArgusClient(http);
    }

    public Task<ApiResult<RegisterAgentResponse>> RegisterAsync(RegisterAgentRequest request, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, AgentApi.Register, request, AgentJsonContext.Default.RegisterAgentRequest,
            AgentJsonContext.Default.RegisterAgentResponse, agentKey: null, cancellationToken);

    public Task<ApiResult<MetricsBatchResponse>> SendMetricsAsync(string agentKey, MetricsBatch batch, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, AgentApi.Metrics, batch, AgentJsonContext.Default.MetricsBatch,
            AgentJsonContext.Default.MetricsBatchResponse, agentKey, cancellationToken);

    public Task<ApiResult<AgentSettings>> SendInventoryAsync(string agentKey, InventoryReport report, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Put, AgentApi.Inventory, report, AgentJsonContext.Default.InventoryReport,
            AgentJsonContext.Default.AgentSettings, agentKey, cancellationToken);

    /// <summary>The update the server wants this agent to install, or null when there is none.</summary>
    public Task<ApiResult<AgentUpdateOffer?>> GetUpdateOfferAsync(string agentKey, CancellationToken cancellationToken) =>
        ExchangeAsync(Request(HttpMethod.Get, AgentApi.UpdateOffer, agentKey), HttpCompletionOption.ResponseContentRead,
            async response => ApiResult<AgentUpdateOffer?>.Success(response.StatusCode == HttpStatusCode.NoContent
                ? null
                : await response.Content.ReadFromJsonAsync(AgentJsonContext.Default.AgentUpdateOffer, cancellationToken)),
            cancellationToken);

    /// <summary>Downloads the offered build to <paramref name="path"/>, keeping it only if its size and SHA-256 match the offer.</summary>
    public Task<ApiResult<bool>> DownloadUpdateAsync(string agentKey, AgentUpdateOffer offer, string path, CancellationToken cancellationToken) =>
        ExchangeAsync(Request(HttpMethod.Get, AgentApi.UpdateDownload, agentKey), HttpCompletionOption.ResponseHeadersRead,
            async response =>
            {
                await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await UpdateFiles.SaveCheckedAsync(body, offer, path, cancellationToken) is { } problem
                    ? ApiResult<bool>.Fail(FailureKind.Rejected, problem, (int)response.StatusCode)
                    : ApiResult<bool>.Success(true);
            },
            cancellationToken);

    public Task<ApiResult<bool>> ReportUpdateResultAsync(string agentKey, AgentUpdateResult result, CancellationToken cancellationToken)
    {
        var request = Request(HttpMethod.Post, AgentApi.UpdateResult, agentKey);
        request.Content = CreateContent(result, AgentJsonContext.Default.AgentUpdateResult);
        return ExchangeAsync(request, HttpCompletionOption.ResponseContentRead,
            _ => Task.FromResult(ApiResult<bool>.Success(true)), cancellationToken);
    }

    private Task<ApiResult<TResponse>> SendAsync<TRequest, TResponse>(
        HttpMethod method,
        string path,
        TRequest body,
        JsonTypeInfo<TRequest> requestType,
        JsonTypeInfo<TResponse> responseType,
        string? agentKey,
        CancellationToken cancellationToken)
    {
        var request = Request(method, path, agentKey);
        request.Content = CreateContent(body, requestType);
        return ExchangeAsync(request, HttpCompletionOption.ResponseContentRead, async response =>
        {
            var value = await response.Content.ReadFromJsonAsync(responseType, cancellationToken);
            return value is null
                ? ApiResult<TResponse>.Fail(FailureKind.ServerError, "The server sent an empty response.", (int)response.StatusCode)
                : ApiResult<TResponse>.Success(value);
        }, cancellationToken);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? agentKey)
    {
        // Relative to the base address, so a server hosted under a path prefix keeps working.
        var request = new HttpRequestMessage(method, path.TrimStart('/'));
        if (agentKey is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agentKey);
        }

        return request;
    }

    /// <summary>Sends the request (and disposes it), reads a successful response, and classifies failures.</summary>
    private async Task<ApiResult<TResponse>> ExchangeAsync<TResponse>(
        HttpRequestMessage request,
        HttpCompletionOption completion,
        Func<HttpResponseMessage, Task<ApiResult<TResponse>>> readSuccess,
        CancellationToken cancellationToken)
    {
        using var disposeRequest = request;
        try
        {
            using var response = await http.SendAsync(request, completion, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await readSuccess(response);
            }

            var status = (int)response.StatusCode;
            var failure = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => FailureKind.Unauthorized,
                HttpStatusCode.TooManyRequests => FailureKind.RateLimited,
                HttpStatusCode.RequestTimeout => FailureKind.ServerError,
                _ when status >= 500 => FailureKind.ServerError,
                _ => FailureKind.Rejected,
            };

            return ApiResult<TResponse>.Fail(failure, await ReadProblemAsync(response, cancellationToken), status, RetryAfter(response));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return ApiResult<TResponse>.Fail(FailureKind.Unreachable, ex.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ApiResult<TResponse>.Fail(FailureKind.Unreachable, "The request timed out.");
        }
        catch (JsonException ex)
        {
            return ApiResult<TResponse>.Fail(FailureKind.ServerError, "Unexpected response: " + ex.Message);
        }
    }

    internal static HttpContent CreateContent<T>(T body, JsonTypeInfo<T> type)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(body, type);
        ByteArrayContent content;
        if (json.Length < CompressionThreshold)
        {
            content = new ByteArrayContent(json);
        }
        else
        {
            using var buffer = new MemoryStream();
            using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
            {
                gzip.Write(json);
            }

            content = new ByteArrayContent(buffer.ToArray());
            content.Headers.ContentEncoding.Add("gzip");
        }

        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response) =>
        response.Headers.RetryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - DateTimeOffset.UtcNow,
            _ => null,
        };

    /// <summary>Pulls the human-readable part out of a ProblemDetails response.</summary>
    private static async Task<string?> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(text))
            {
                return response.ReasonPhrase;
            }

            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            return Field(root, "detail") ?? Field(root, "title") ?? response.ReasonPhrase;
        }
        catch (JsonException)
        {
            return response.ReasonPhrase;
        }

        static string? Field(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}
