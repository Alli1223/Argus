using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Argus.Contracts.Agent;
using Argus.Server.Data;
using Argus.Server.Features.Enrollment;
using Argus.Server.Infrastructure;
using Argus.Server.Tests.Enrollment;
using Argus.Server.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Argus.Server.Tests.Agents;

public sealed class AgentRegistrationTests(ArgusAppFixture app) : IClassFixture<ArgusAppFixture>
{
    internal static RegisterAgentRequest Registration(string token, string machineId, string hostname = "web-1") => new()
    {
        EnrollmentToken = token,
        MachineId = machineId,
        AgentVersion = "1.0.0",
        SystemInfo = new SystemInfo
        {
            Hostname = hostname,
            Platform = HostPlatform.Linux,
            Architecture = "x64",
            OsName = "Ubuntu 24.04 LTS",
            CpuLogicalProcessors = 4,
            MemoryTotalBytes = 8L << 30,
            IpAddresses = ["10.0.0.5"],
        },
    };

    private async Task<(HttpClient Owner, CreatedEnrollmentToken Token)> OwnerWithTokenAsync(string email, object? tokenRequest = null)
    {
        await app.CreateUserAsync(email, EnrollmentTokenTests.Password);
        var owner = await app.CreateSignedInClientAsync(email, EnrollmentTokenTests.Password);
        return (owner, await EnrollmentTokenTests.CreateTokenAsync(owner, tokenRequest));
    }

    /// <summary>Agents send no cookies and no CSRF header, just like the real agent.</summary>
    private Task<HttpResponseMessage> RegisterAsync(RegisterAgentRequest request) =>
        app.Factory.CreateClient().PostAsJsonAsync(
            AgentApi.Register, request, AgentJsonContext.Default.RegisterAgentRequest, TestContext.Current.CancellationToken);

    private async Task<RegisterAgentResponse> RegisterOkAsync(RegisterAgentRequest request)
    {
        var response = await RegisterAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync(AgentJsonContext.Default.RegisterAgentResponse, TestContext.Current.CancellationToken))!;
    }

    private Task<HttpResponseMessage> SendInventoryAsync(HttpClient client, string hostname = "web-1", string osName = "Ubuntu 24.04.1 LTS") =>
        client.PutAsJsonAsync(
            AgentApi.Inventory,
            new InventoryReport { AgentVersion = "1.1.0", SystemInfo = Registration("", "", hostname).SystemInfo with { OsName = osName } },
            AgentJsonContext.Default.InventoryReport,
            TestContext.Current.CancellationToken);

    private HttpClient AgentClient(string agentKey)
    {
        var client = app.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", agentKey);
        return client;
    }

    [Fact]
    public async Task Agents_exchange_an_enrollment_token_for_a_host_and_key()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, token) = await OwnerWithTokenAsync("sam@example.com", new { name = "Fleet", tags = new[] { "prod" } });

        var registered = await RegisterOkAsync(Registration(token.Token, "machine-a", "web-1"));

        Assert.True(SecretTokens.LooksValid(registered.AgentKey, AgentApi.AgentKeyPrefix));
        Assert.Equal(15, registered.Settings.CollectionIntervalSeconds);

        var host = await app.WithScopeAsync(services =>
            services.GetRequiredService<ArgusDbContext>().Hosts.AsNoTracking().SingleAsync(h => h.Id == registered.HostId, ct));
        Assert.Equal("web-1", host.DisplayName);
        Assert.Equal(HostPlatform.Linux, host.Platform);
        Assert.Equal(["prod"], host.Tags);
        Assert.Equal(SecretTokens.Hash(registered.AgentKey), host.AgentKeyHash);

        var summary = Assert.Single((await owner.GetFromJsonAsync<List<EnrollmentTokenSummary>>("/api/enrollment-tokens", ct))!);
        Assert.Equal(1, summary.UseCount);
    }

    [Fact]
    public async Task Unusable_tokens_are_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, single) = await OwnerWithTokenAsync("tess@example.com", new { name = "Once", maxUses = 1 });
        var revoked = await EnrollmentTokenTests.CreateTokenAsync(owner);
        var expired = await EnrollmentTokenTests.CreateTokenAsync(owner);
        await owner.DeleteAsync($"/api/enrollment-tokens/{revoked.Summary.Id}", ct);
        await app.WithScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ArgusDbContext>();
            var stored = await db.EnrollmentTokens.SingleAsync(t => t.Id == expired.Summary.Id, ct);
            stored.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            return await db.SaveChangesAsync(ct);
        });

        await RegisterOkAsync(Registration(single.Token, "machine-1"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await RegisterAsync(Registration(single.Token, "machine-2"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RegisterAsync(Registration(revoked.Token, "machine-3"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RegisterAsync(Registration(expired.Token, "machine-4"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RegisterAsync(Registration(SecretTokens.Generate(AgentApi.EnrollmentTokenPrefix), "machine-5"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RegisterAsync(Registration("garbage", "machine-6"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await RegisterAsync(Registration(single.Token, ""))).StatusCode);
    }

    [Fact]
    public async Task Re_registering_a_machine_keeps_the_host_and_replaces_its_key()
    {
        var (_, token) = await OwnerWithTokenAsync("uma@example.com");
        var first = await RegisterOkAsync(Registration(token.Token, "machine-r"));
        Assert.Equal(HttpStatusCode.OK, (await SendInventoryAsync(AgentClient(first.AgentKey))).StatusCode);

        var second = await RegisterOkAsync(Registration(token.Token, "machine-r"));

        Assert.Equal(first.HostId, second.HostId);
        Assert.NotEqual(first.AgentKey, second.AgentKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendInventoryAsync(AgentClient(first.AgentKey))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendInventoryAsync(AgentClient(second.AgentKey))).StatusCode);
    }

    [Fact]
    public async Task Inventory_updates_need_an_agent_key_not_a_browser_session()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, token) = await OwnerWithTokenAsync("vic@example.com");
        var registered = await RegisterOkAsync(Registration(token.Token, "machine-v"));

        var response = await SendInventoryAsync(AgentClient(registered.AgentKey), osName: "Ubuntu 26.04 LTS");
        response.EnsureSuccessStatusCode();
        var settings = await response.Content.ReadFromJsonAsync(AgentJsonContext.Default.AgentSettings, ct);
        Assert.Equal(15, settings!.CollectionIntervalSeconds);
        var host = await app.WithScopeAsync(services =>
            services.GetRequiredService<ArgusDbContext>().Hosts.AsNoTracking().SingleAsync(h => h.Id == registered.HostId, ct));
        Assert.Equal("Ubuntu 26.04 LTS", host.OsName);
        Assert.Equal("1.1.0", host.AgentVersion);

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendInventoryAsync(app.Factory.CreateClient())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendInventoryAsync(owner)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendInventoryAsync(AgentClient(SecretTokens.Generate(AgentApi.AgentKeyPrefix)))).StatusCode);
    }
}
