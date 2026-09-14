namespace Argus.Server.Tests.Infrastructure;

/// <summary>A started server with its own database, shared by the tests of one class.</summary>
public sealed class ArgusAppFixture(PostgresFixture postgres) : IAsyncLifetime
{
    public ArgusFactory Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Factory = new ArgusFactory(await postgres.CreateDatabaseAsync());

        // Starting the server applies migrations, so failures surface here rather than mid-test.
        _ = Factory.Server;
    }

    public HttpClient CreateClient() => Factory.CreateClient();

    public async ValueTask DisposeAsync() => await Factory.DisposeAsync();
}
