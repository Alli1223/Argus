using System.Net;
using System.Net.Http.Json;
using Argus.Server.Features.Info;
using Argus.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Argus.Server.Tests.Downloads;

public sealed class DownloadsTests(ArgusAppFixture app) : IClassFixture<ArgusAppFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/downloads/install.sh", "text/x-shellscript", "systemctl enable --now")]
    [InlineData("/downloads/uninstall.sh", "text/x-shellscript", "--purge")]
    [InlineData("/downloads/argus-agent.service", "text/plain", "ExecStart=/opt/argus-agent/argus-agent run")]
    [InlineData("/downloads/install.ps1", "text/plain", "New-Service -Name $serviceName")]
    [InlineData("/downloads/uninstall.ps1", "text/plain", "sc.exe delete")]
    public async Task Install_scripts_are_public(string path, string contentType, string expected)
    {
        var response = await app.Factory.CreateClient().GetAsync(path, Ct);

        response.EnsureSuccessStatusCode();
        Assert.Equal(contentType, response.Content.Headers.ContentType!.MediaType);
        Assert.Contains(expected, await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_agent_builds_explain_themselves()
    {
        var response = await app.Factory.CreateClient().GetAsync("/downloads/agent/linux-x64/argus-agent", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Agent build not available", (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Title);
    }

    [Theory]
    [InlineData("/downloads/agent/linux-x64/argus-agent.exe")]
    [InlineData("/downloads/agent/solaris-sparc/argus-agent")]
    [InlineData("/downloads/agent/win-x64/argus-agent")]
    public async Task Only_known_builds_can_be_requested(string path)
    {
        var response = await app.Factory.CreateClient().GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Server_info_is_public()
    {
        var info = await app.Factory.CreateClient().GetFromJsonAsync<ServerInfo>("/api/info", Ct);

        Assert.Equal("Argus", info!.Name);
        Assert.Null(info.PublicUrl);
    }
}
