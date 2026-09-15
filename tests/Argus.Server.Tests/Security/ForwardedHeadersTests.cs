using System.Net;
using Argus.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Argus.Server.Tests.Security;

/// <summary>A server behind a reverse proxy at 10.0.0.2.</summary>
public sealed class TrustedProxyFixture(PostgresFixture postgres) : ArgusAppFixture(postgres)
{
    protected override IReadOnlyDictionary<string, string?> Settings { get; } =
        new Dictionary<string, string?> { ["Argus:Proxy:TrustedProxies:0"] = "10.0.0.2" };
}

public sealed class ForwardedHeadersTests(TrustedProxyFixture app) : IClassFixture<TrustedProxyFixture>
{
    private Task<HttpContext> RequestFromAsync(string remoteAddress) =>
        app.Factory.Server.SendAsync(context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/health/live";
            context.Request.Host = new HostString("argus.example.com");
            context.Request.Headers["X-Forwarded-Proto"] = "https";
            context.Request.Headers["X-Forwarded-For"] = "203.0.113.7";
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Requests_through_the_proxy_carry_the_clients_scheme_and_address()
    {
        var context = await RequestFromAsync("10.0.0.2");

        Assert.Equal("https", context.Request.Scheme);
        Assert.Equal(IPAddress.Parse("203.0.113.7"), context.Connection.RemoteIpAddress);
        Assert.False(string.IsNullOrEmpty(context.Response.Headers.StrictTransportSecurity));
    }

    [Fact]
    public async Task Anyone_else_cannot_claim_a_scheme_or_address()
    {
        var context = await RequestFromAsync("10.0.0.9");

        Assert.Equal("http", context.Request.Scheme);
        Assert.Equal(IPAddress.Parse("10.0.0.9"), context.Connection.RemoteIpAddress);
        Assert.True(string.IsNullOrEmpty(context.Response.Headers.StrictTransportSecurity));
    }
}
