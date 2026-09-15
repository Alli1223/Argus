using System.Net;
using System.Net.Http.Json;
using Argus.Contracts.Agent;
using Argus.Server.Features.Auth;
using Argus.Server.Features.Users;
using Argus.Server.Tests.Infrastructure;
using Argus.Server.Tests.Metrics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Argus.Server.Tests.Users;

public sealed class UserAdminTests(ArgusAppFixture app) : IClassFixture<ArgusAppFixture>
{
    private const string Password = "correct horse battery";

    private async Task<HttpClient> SignedInAdminAsync()
    {
        var email = $"admin-{Guid.NewGuid():N}@example.com";
        await app.CreateUserAsync(email, Password, Roles.Admin);
        return await app.CreateSignedInClientAsync(email, Password);
    }

    private static async Task<UserSummary> CreateAsync(HttpClient admin, string email, string role = Roles.User)
    {
        var response = await admin.PostAsJsonAsync("/api/users",
            new { email, displayName = email.Split('@')[0], password = Password, role }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UserSummary>(TestContext.Current.CancellationToken))!;
    }

    private async Task<HttpStatusCode> LoginStatusAsync(string email, string password)
    {
        var response = await app.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password }, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    [Fact]
    public async Task Only_admins_can_manage_users()
    {
        var ct = TestContext.Current.CancellationToken;
        await app.CreateUserAsync("regular@example.com", Password);
        var regular = await app.CreateSignedInClientAsync("regular@example.com", Password);

        Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().GetAsync("/api/users", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await regular.GetAsync("/api/users", ct)).StatusCode);
    }

    [Fact]
    public async Task Admins_create_and_list_users()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await SignedInAdminAsync();

        var created = await CreateAsync(admin, "ivy@example.com");
        Assert.Equal(Roles.User, created.Role);
        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync("ivy@example.com", Password));

        var duplicate = await admin.PostAsJsonAsync("/api/users",
            new { email = "ivy@example.com", displayName = "Ivy", password = Password, role = Roles.User }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        var invalidRole = await admin.PostAsJsonAsync("/api/users",
            new { email = "jay@example.com", displayName = "Jay", password = Password, role = "Superuser" }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalidRole.StatusCode);

        var list = await admin.GetFromJsonAsync<List<UserSummary>>("/api/users", ct);
        Assert.Contains(list!, user => user.Email == "ivy@example.com" && user.Role == Roles.User);
    }

    [Fact]
    public async Task Admins_change_names_and_roles()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await SignedInAdminAsync();
        var user = await CreateAsync(admin, "kim@example.com");

        var response = await admin.PutAsJsonAsync($"/api/users/{user.Id}", new { displayName = "Kim K", role = Roles.Admin }, ct);

        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<UserSummary>(ct);
        Assert.Equal("Kim K", updated!.DisplayName);
        Assert.Equal(Roles.Admin, updated.Role);
        var kim = await app.CreateSignedInClientAsync("kim@example.com", Password);
        Assert.True((await kim.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me", ct))!.IsAdmin);
    }

    [Fact]
    public async Task Disabled_users_cannot_sign_in_until_enabled()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await SignedInAdminAsync();
        var user = await CreateAsync(admin, "lee@example.com");

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/users/{user.Id}/disable", null, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatusAsync("lee@example.com", Password));
        var list = await admin.GetFromJsonAsync<List<UserSummary>>("/api/users", ct);
        Assert.True(list!.Single(u => u.Id == user.Id).IsDisabled);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/users/{user.Id}/enable", null, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync("lee@example.com", Password));
    }

    [Fact]
    public async Task Admins_reset_passwords()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await SignedInAdminAsync();
        var user = await CreateAsync(admin, "max@example.com");

        var weak = await admin.PostAsJsonAsync($"/api/users/{user.Id}/reset-password", new { newPassword = "short" }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Contains("newPassword", (await weak.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(ct))!.Errors.Keys);

        var reset = await admin.PostAsJsonAsync($"/api/users/{user.Id}/reset-password", new { newPassword = "temporary passphrase" }, ct);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatusAsync("max@example.com", Password));
        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync("max@example.com", "temporary passphrase"));
    }

    [Fact]
    public async Task Admins_delete_users_but_not_themselves()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await SignedInAdminAsync();
        var me = await admin.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me", ct);
        var user = await CreateAsync(admin, "ned@example.com");

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/users/{user.Id}", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatusAsync("ned@example.com", Password));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/users/{user.Id}", ct)).StatusCode);

        var self = await admin.DeleteAsync($"/api/users/{me!.Id}", ct);
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/users/{me.Id}/disable", null, ct)).StatusCode);
    }

    [Fact]
    public async Task Deleting_someone_removes_their_hosts_history_and_agent_keys()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await SignedInAdminAsync();
        var owner = await app.CreateOwnerAsync("oli@example.com");
        var ownerId = (await owner.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me", ct))!.Id;
        var (hostId, agent) = await app.RegisterHostAsync(owner, "users-oli-1");
        await agent.SendSamplesAsync(MetricsIngestionTests.FullSample(DateTimeOffset.UtcNow));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/users/{ownerId}", ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/hosts/{hostId}", ct)).StatusCode);
        var rejected = await agent.PostAsJsonAsync(AgentApi.Metrics,
            new MetricsBatch { Samples = [MetricsIngestionTests.FullSample(DateTimeOffset.UtcNow)] },
            AgentJsonContext.Default.MetricsBatch, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);

        var remaining = await app.WithScopeAsync(async services =>
        {
            await using var command = services.GetRequiredService<NpgsqlDataSource>().CreateCommand(
                "SELECT (SELECT count(*) FROM host_metrics WHERE host_id = @id) + (SELECT count(*) FROM filesystem_metrics WHERE host_id = @id)");
            command.Parameters.AddWithValue("id", hostId);
            return (long)(await command.ExecuteScalarAsync(ct))!;
        });
        Assert.Equal(0, remaining);
    }
}

public sealed class LastAdminTests(ArgusAppFixture app) : IClassFixture<ArgusAppFixture>
{
    [Fact]
    public async Task The_last_active_admin_cannot_be_demoted()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = await app.CreateUserAsync("owner@example.com", "correct horse battery", Roles.Admin);
        var client = await app.CreateSignedInClientAsync("owner@example.com", "correct horse battery");

        var response = await client.PutAsJsonAsync($"/api/users/{owner.Id}", new { displayName = "Owner", role = Roles.User }, ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Last administrator", (await response.Content.ReadFromJsonAsync<ProblemDetails>(ct))!.Title);
    }
}
