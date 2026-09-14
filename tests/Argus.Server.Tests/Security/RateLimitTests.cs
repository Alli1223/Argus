using System.Net;
using System.Net.Http.Json;
using Argus.Server.Tests.Infrastructure;

namespace Argus.Server.Tests.Security;

public sealed class RateLimitTests(ThrottledFixture app) : IClassFixture<ThrottledFixture>
{
    [Fact]
    public async Task Sign_in_attempts_are_throttled()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = app.CreateClient();
        var attempt = new { email = "x@example.com", password = "wrong password" };

        for (var i = 0; i < 3; i++)
        {
            var allowed = await client.PostAsJsonAsync("/api/auth/login", attempt, ct);
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        var throttled = await client.PostAsJsonAsync("/api/auth/login", attempt, ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.NotNull(throttled.Headers.RetryAfter);
    }
}
