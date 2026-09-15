using Argus.Server.Tests.Infrastructure;

namespace Argus.Server.Tests.Security;

public sealed class SecurityHeadersTests(ArgusAppFixture app) : IClassFixture<ArgusAppFixture>
{
    [Theory]
    [InlineData("/api/info")]
    [InlineData("/api/auth/me")]
    [InlineData("/api/does-not-exist")]
    public async Task Responses_carry_hardening_headers(string path)
    {
        var response = await app.CreateClient().GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Contains("frame-ancestors 'none'", Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }
}
