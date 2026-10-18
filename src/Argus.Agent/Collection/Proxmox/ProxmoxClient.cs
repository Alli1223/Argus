using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Argus.Agent.Collection.Proxmox;

/// <summary>Proxmox VE answered with an error, or could not be reached at all.</summary>
internal sealed class ProxmoxException(string message, HttpStatusCode? status = null, Exception? inner = null)
    : Exception(message, inner)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>The few calls of Proxmox VE's API the agent makes, with an API token.</summary>
internal sealed class ProxmoxClient : IDisposable
{
    private readonly HttpClient _http;

    public ProxmoxClient(string url, string token, string? fingerprint)
        : this(CreateHandler(new Uri(url), fingerprint), url, token)
    {
    }

    /// <summary>For tests: a client whose requests go to <paramref name="handler"/>.</summary>
    internal ProxmoxClient(HttpMessageHandler handler, string url, string token)
    {
        _http = new HttpClient(handler) { BaseAddress = new Uri(url.TrimEnd('/') + "/api2/json/"), Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("PVEAPIToken", token.Trim());
    }

    /// <summary>The <c>data</c> of Proxmox VE's answer.</summary>
    public async Task<JsonElement> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, cancellationToken);
        return await DataAsync(response, cancellationToken);
    }

    /// <summary>Asks for something done; Proxmox VE answers with the id of the task (UPID) doing it.</summary>
    public async Task<string> PostAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, path, cancellationToken);
        var data = await DataAsync(response, cancellationToken);
        return data.ValueKind == JsonValueKind.String ? data.GetString()! : "";
    }

    public void Dispose() => _http.Dispose();

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            // Proxmox VE wants a body with every POST, even an empty one.
            var request = new HttpRequestMessage(method, path);
            if (method == HttpMethod.Post)
            {
                request.Content = new FormUrlEncodedContent([]);
            }

            response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ProxmoxException(Describe(ex), inner: ex);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            throw new ProxmoxException(await ErrorMessageAsync(response, cancellationToken), response.StatusCode);
        }
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        return json.RootElement.TryGetProperty("data", out var data) ? data.Clone() : default;
    }

    /// <summary>Proxmox VE puts its reason in the status line, and sometimes in "message" or per-parameter "errors".</summary>
    private static async Task<string> ErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return "Proxmox VE refused the agent's API token. Run the agent's install command again with --proxmox to make a new one.";
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return "The agent's Proxmox VE API token lacks a privilege it needs. Run the agent's install command again with --proxmox " +
                "(and --proxmox-actions to start and stop guests).";
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var json = JsonDocument.Parse(text);
            if (json.RootElement.TryGetProperty("message", out var message) && message.GetString() is { Length: > 0 } detail)
            {
                return detail.Trim();
            }
        }
        catch (JsonException)
        {
        }

        return response.ReasonPhrase is { Length: > 0 } reason
            ? $"Proxmox VE answered {(int)response.StatusCode}: {reason}"
            : $"Proxmox VE answered {(int)response.StatusCode}.";
    }

    private static string Describe(HttpRequestException ex) => ex.InnerException switch
    {
        SocketException { SocketErrorCode: SocketError.ConnectionRefused } => "Proxmox VE's API is not running (nothing answers on its port).",
        AuthenticationException => $"Proxmox VE's certificate was not trusted: {ex.InnerException.Message} Set ProxmoxFingerprint to its SHA-256 fingerprint.",
        _ => $"Proxmox VE could not be reached: {ex.Message}",
    };

    /// <summary>
    /// Proxmox VE signs its own certificate. On this machine that is no risk, so any certificate is accepted
    /// there; elsewhere the certificate must be trusted, or match the fingerprint given.
    /// </summary>
    private static SocketsHttpHandler CreateHandler(Uri url, string? fingerprint)
    {
        var pinned = NormalizeFingerprint(fingerprint);
        return new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                    errors == SslPolicyErrors.None
                    || url.IsLoopback
                    || (pinned is not null && certificate is not null && Fingerprint(certificate) == pinned),
            },
        };
    }

    internal static string? NormalizeFingerprint(string? fingerprint) =>
        string.IsNullOrWhiteSpace(fingerprint) ? null : fingerprint.Replace(":", "", StringComparison.Ordinal).Trim().ToUpperInvariant();

    private static string Fingerprint(X509Certificate certificate) => Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
}
