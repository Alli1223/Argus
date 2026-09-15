using System.Net.Http.Json;
using Argus.Server.Features.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Argus.Server.Tests.Infrastructure;

/// <summary>A started server with its own database, shared by the tests of one class.</summary>
public class ArgusAppFixture(PostgresFixture postgres) : IAsyncLifetime
{
    public const string CsrfHeader = "X-Argus-Csrf";

    public ArgusFactory Factory { get; private set; } = null!;

    /// <summary>Extra configuration for the server under test.</summary>
    protected virtual IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>();

    /// <summary>Replaces services of the server under test (applied after the app's own registrations).</summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    public async ValueTask InitializeAsync()
    {
        Factory = new ArgusFactory(await postgres.CreateDatabaseAsync(), Settings, ConfigureServices);

        // Starting the server applies migrations, so failures surface here rather than mid-test.
        _ = Factory.Server;
    }

    /// <summary>A client with its own cookie jar that behaves like the web UI.</summary>
    public HttpClient CreateClient()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(CsrfHeader, "1");
        return client;
    }

    public async Task<HttpClient> CreateSignedInClientAsync(string email, string password)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return client;
    }

    public async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public Task<ArgusUser> CreateUserAsync(string email, string password, string role = Roles.User) =>
        WithScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ArgusUser>>();
            var user = new ArgusUser
            {
                UserName = email,
                Email = email,
                DisplayName = email.Split('@')[0],
                CreatedAt = DateTimeOffset.UtcNow,
            };

            var result = await users.CreateAsync(user, password);
            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
            await users.AddToRoleAsync(user, role);
            return user;
        });

    public virtual async ValueTask DisposeAsync() => await Factory.DisposeAsync();
}

/// <summary>A server with self-registration switched on.</summary>
public sealed class OpenRegistrationFixture(PostgresFixture postgres) : ArgusAppFixture(postgres)
{
    protected override IReadOnlyDictionary<string, string?> Settings { get; } =
        new Dictionary<string, string?> { ["Argus:Auth:AllowRegistration"] = "true" };
}

/// <summary>A server with a tiny sign-in rate limit.</summary>
public sealed class ThrottledFixture(PostgresFixture postgres) : ArgusAppFixture(postgres)
{
    protected override IReadOnlyDictionary<string, string?> Settings { get; } =
        new Dictionary<string, string?> { ["Argus:RateLimits:AuthPermitsPerMinute"] = "3" };
}
