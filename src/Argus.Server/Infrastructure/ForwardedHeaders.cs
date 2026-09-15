using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Argus.Server.Infrastructure;

/// <summary>
/// Reverse proxies (such as Caddy in the compose file) whose X-Forwarded-For and X-Forwarded-Proto
/// headers are believed. Nothing is trusted by default: a client talking to the server directly could
/// otherwise claim any address, and with it slip past the per-address rate limits.
/// </summary>
public sealed class ProxyOptions : IValidatableObject
{
    public const string SectionName = "Argus:Proxy";

    /// <summary>Addresses of individual proxies, such as "10.0.0.2".</summary>
    public List<string> TrustedProxies { get; set; } = [];

    /// <summary>Networks the proxies connect from, in CIDR notation, such as "172.30.0.0/24".</summary>
    public List<string> TrustedNetworks { get; set; } = [];

    public bool Enabled => TrustedProxies.Count > 0 || TrustedNetworks.Count > 0;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var proxy in TrustedProxies.Where(proxy => !IPAddress.TryParse(proxy, out _)))
        {
            yield return new ValidationResult($"'{proxy}' is not an IP address.", [nameof(TrustedProxies)]);
        }

        foreach (var network in TrustedNetworks.Where(network => !System.Net.IPNetwork.TryParse(network, out _)))
        {
            yield return new ValidationResult(
                $"'{network}' is not a network in CIDR notation, such as 10.0.0.0/8.", [nameof(TrustedNetworks)]);
        }
    }
}

public static class ForwardedHeadersSetup
{
    public static IServiceCollection AddArgusForwardedHeaders(this IServiceCollection services)
    {
        services.AddValidatedOptions<ProxyOptions>(ProxyOptions.SectionName);
        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<ProxyOptions>>((forwarded, proxy) =>
        {
            forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // Only the configured proxies, not the loopback defaults.
            forwarded.KnownProxies.Clear();
            forwarded.KnownIPNetworks.Clear();
            foreach (var address in proxy.Value.TrustedProxies)
            {
                forwarded.KnownProxies.Add(IPAddress.Parse(address));
            }

            foreach (var network in proxy.Value.TrustedNetworks)
            {
                forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });
        return services;
    }

    /// <summary>
    /// Applies forwarded headers from trusted proxies. It runs first, so everything after it (HSTS, the
    /// secure cookie, rate limits) sees the client's own scheme and address.
    /// </summary>
    public static WebApplication UseArgusForwardedHeaders(this WebApplication app)
    {
        if (app.Services.GetRequiredService<IOptions<ProxyOptions>>().Value.Enabled)
        {
            app.UseForwardedHeaders();
        }

        return app;
    }
}
