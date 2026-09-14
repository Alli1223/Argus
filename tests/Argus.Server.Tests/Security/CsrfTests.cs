using System.Net;
using System.Net.Http.Json;
using Argus.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Argus.Server.Tests.Security;

public sealed class CsrfTests(ArgusAppFixture app) : IClassFixture<ArgusAppFixture>
{
    [Fact]
    public async Task Unsafe_api_requests_without_the_header_are_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = app.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "x@example.com", password = "whatever you like" }, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Missing anti-forgery header", (await response.Content.ReadFromJsonAsync<ProblemDetails>(ct))!.Title);
    }

    [Fact]
    public async Task Safe_requests_do_not_need_the_header()
    {
        var response = await app.Factory.CreateClient().GetAsync("/api/auth/status", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }
}
