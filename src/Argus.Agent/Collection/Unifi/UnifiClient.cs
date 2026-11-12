using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Argus.Agent.Collection.Unifi;

/// <summary>The UniFi console answered with an error, or could not be reached at all.</summary>
internal sealed class UnifiException(string message, HttpStatusCode? status = null, Exception? inner = null)
    : Exception(message, inner)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>
/// Read-only calls to a UniFi OS console's Network application, with an API key. The key works for both
/// the documented integration API and the older API the web interface uses, which has the traffic figures.
/// </summary>
internal sealed class UnifiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly CertificateCheck? _certificate;

    public UnifiClient(string url, string apiKey, string? fingerprint)
        : this(CreateHandler(fingerprint, out var check), url, apiKey)
    {
        _certificate = check;
    }

    /// <summary>For tests: a client whose requests go to <paramref name="handler"/>.</summary>
    internal UnifiClient(HttpMessageHandler handler, string url, string apiKey)
    {
        _http = new HttpClient(handler) { BaseAddress = new Uri(url.TrimEnd('/') + "/proxy/network/"), Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.Add("X-API-KEY", apiKey.Trim());
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    /// <summary>The whole JSON answer to a GET of <paramref name="path"/>, relative to the Network application.</summary>
    public async Task<JsonElement> GetAsync(string path, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(path, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new UnifiException(Describe(ex), inner: ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new UnifiException(ErrorMessage(response), response.StatusCode);
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            return json.RootElement.Clone();
        }
    }

    public void Dispose() => _http.Dispose();

    private static string ErrorMessage(HttpResponseMessage response) => response.StatusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            "The UniFi console refused the agent's API key. Make one in UniFi Network under Settings → Control Plane → " +
            "Integrations (not in the cloud Site Manager) and set it as UnifiApiKey.",
        HttpStatusCode.NotFound => "The UniFi console has no such site or API. Check UnifiSite, and that UniFi Network is 9.0 or later.",
        _ => response.ReasonPhrase is { Length: > 0 } reason
            ? $"The UniFi console answered {(int)response.StatusCode}: {reason}"
            : $"The UniFi console answered {(int)response.StatusCode}.",
    };

    private string Describe(HttpRequestException ex) => ex.InnerException switch
    {
        SocketException { SocketErrorCode: SocketError.ConnectionRefused } => "Nothing answers at the UniFi console's address.",
        AuthenticationException when _certificate?.Presented is { } presented =>
            $"The UniFi console's certificate is not trusted. If it is your console, set UnifiFingerprint to {presented}.",
        AuthenticationException => $"The UniFi console's certificate is not trusted: {ex.InnerException.Message}",
        _ => $"The UniFi console could not be reached: {ex.Message}",
    };

    /// <summary>Remembers the certificate the console last presented, so a refusal can say what to pin.</summary>
    private sealed class CertificateCheck(string? pinned)
    {
        public string? Presented { get; private set; }

        public bool Validate(X509Certificate? certificate, SslPolicyErrors errors)
        {
            if (certificate is null)
            {
                return false;
            }

            var fingerprint = Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
            Presented = string.Join(':', fingerprint.Chunk(2).Select(pair => new string(pair)));
            return errors == SslPolicyErrors.None || fingerprint == pinned;
        }
    }

    private static SocketsHttpHandler CreateHandler(string? fingerprint, out CertificateCheck check)
    {
        var certificate = check = new CertificateCheck(NormalizeFingerprint(fingerprint));
        return new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, presented, _, errors) => certificate.Validate(presented, errors),
            },
        };
    }

    internal static string? NormalizeFingerprint(string? fingerprint) =>
        string.IsNullOrWhiteSpace(fingerprint) ? null : fingerprint.Replace(":", "", StringComparison.Ordinal).Trim().ToUpperInvariant();
}
