using Argus.Server.Features.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Argus.Server.Tests.Infrastructure;

/// <summary>A started server with its own database, shared by the tests of one class.</summary>
public sealed class ArgusAppFixture(PostgresFixture postgres) : IAsyncLifetime
{
    public const string CsrfHeader = "X-Argus-Csrf";

    public ArgusFactory Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Factory = new ArgusFactory(await postgres.CreateDatabaseAsync());

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

    public async ValueTask DisposeAsync() => await Factory.DisposeAsync();
}
