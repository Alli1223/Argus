using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Argus.Agent.Collection.Containers;

/// <summary>Docker answered with an error, or could not be reached at all.</summary>
internal sealed class DockerException(string message, HttpStatusCode? status = null, Exception? inner = null)
    : Exception(message, inner)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>The few calls of Docker's Engine API the agent makes, over its Unix socket.</summary>
internal sealed class DockerClient : IDisposable
{
    private readonly HttpClient _http;

    public DockerClient(string socketPath)
        : this(new SocketsHttpHandler
        {
            ConnectCallback = async (_, cancellationToken) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        })
    {
    }

    /// <summary>For tests: a client whose requests go to <paramref name="handler"/>.</summary>
    internal DockerClient(HttpMessageHandler handler)
    {
        _http = new HttpClient(handler) { BaseAddress = new Uri("http://docker/"), Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, HttpCompletionOption.ResponseContentRead, cancellationToken);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
    }

    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, HttpCompletionOption completion, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(new HttpRequestMessage(method, path), completion, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new DockerException(Describe(ex), inner: ex);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            throw new DockerException(await ErrorMessageAsync(response, cancellationToken), response.StatusCode);
        }
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Docker explains its errors as {"message": "..."}.</summary>
    private static async Task<string> ErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var json = JsonDocument.Parse(text);
            if (json.RootElement.TryGetProperty("message", out var message) && message.GetString() is { Length: > 0 } detail)
            {
                return detail;
            }
        }
        catch (JsonException)
        {
        }

        return $"Docker answered {(int)response.StatusCode} {response.ReasonPhrase}.";
    }

    private static string Describe(HttpRequestException ex) => ex.InnerException switch
    {
        SocketException { SocketErrorCode: SocketError.AccessDenied } =>
            "The agent is not allowed to use Docker's socket. Add its user to the docker group (see the install script's --docker option).",
        SocketException { SocketErrorCode: SocketError.AddressNotAvailable or SocketError.ConnectionRefused } =>
            "Docker is not running.",
        _ => $"Docker could not be reached: {ex.Message}",
    };
}
