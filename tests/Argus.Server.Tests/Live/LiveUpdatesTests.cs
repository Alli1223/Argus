using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Argus.Contracts.Agent;
using Argus.Server.Features.Alerts;
using Argus.Server.Features.Auth;
using Argus.Server.Features.Hosts;
using Argus.Server.Features.Live;
using Argus.Server.Tests.Infrastructure;
using Argus.Server.Tests.Metrics;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Argus.Server.Tests.Live;

public sealed class LiveUpdatesTests(AlertsFixture app) : IClassFixture<AlertsFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Session(HubConnection Connection, HttpClient Client, Guid UserId) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Connection.DisposeAsync();
    }

    /// <summary>Signs a new user in and opens a hub connection with their session cookie, as a browser would.</summary>
    private async Task<Session> ConnectAsync(string email, string role = Roles.User)
    {
        var user = await app.CreateUserAsync(email, AgentTestHelpers.Password, role);

        var login = app.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        login.DefaultRequestHeaders.Add(ArgusAppFixture.CsrfHeader, "1");
        var response = await login.PostAsJsonAsync("/api/auth/login", new { email, password = AgentTestHelpers.Password }, Ct);
        response.EnsureSuccessStatusCode();
        var cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("argus_session=", StringComparison.Ordinal));

        var connection = BuildConnection(cookie.Split(';')[0]);
        await connection.StartAsync(Ct);
        return new Session(connection, await app.CreateSignedInClientAsync(email, AgentTestHelpers.Password), user.Id);
    }

    private HubConnection BuildConnection(string? cookie) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(app.Factory.Server.BaseAddress, "hubs/live"), options =>
            {
                options.HttpMessageHandlerFactory = _ => app.Factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                if (cookie is not null)
                {
                    options.Headers["Cookie"] = cookie;
                }
            })
            .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();

    private static Channel<T> Listen<T>(HubConnection connection, string method)
    {
        var channel = Channel.CreateUnbounded<T>();
        connection.On<T>(method, message => channel.Writer.TryWrite(message));
        return channel;
    }

    /// <summary>The next message matching <paramref name="match"/>; other hosts' traffic is skipped.</summary>
    private static async Task<T> NextAsync<T>(Channel<T> channel, Func<T, bool> match)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (true)
        {
            var message = await channel.Reader.ReadAsync(timeout.Token);
            if (match(message))
            {
                return message;
            }
        }
    }

    [Fact]
    public async Task Owners_and_admins_see_live_metrics_but_nobody_else_does()
    {
        await using var owner = await ConnectAsync("live-a@example.com");
        await using var admin = await ConnectAsync("live-admin@example.com", Roles.Admin);
        await using var stranger = await ConnectAsync("live-b@example.com");
        var ownerUpdates = Listen<LiveHostMetrics>(owner.Connection, "HostMetrics");
        var adminUpdates = Listen<LiveHostMetrics>(admin.Connection, "HostMetrics");
        var strangerUpdates = Listen<LiveHostMetrics>(stranger.Connection, "HostMetrics");
        var (hostId, agent) = await app.RegisterHostAsync(owner.Client, "live-a-1");

        await agent.SendSamplesAsync(MetricsIngestionTests.FullSample(app.Time.GetUtcNow()));

        var update = await NextAsync(ownerUpdates, message => message.HostId == hostId);
        Assert.Equal(42, update.Latest.CpuPercent, precision: 3);
        Assert.Equal(40, update.Latest.MemoryPercent, precision: 3);
        await NextAsync(adminUpdates, message => message.HostId == hostId);

        // The owner's message has arrived, so a stranger's copy would have too.
        Assert.False(strangerUpdates.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Alert_changes_are_pushed_to_the_owner()
    {
        await using var owner = await ConnectAsync("live-c@example.com");
        var alerts = Listen<LiveAlert>(owner.Connection, "AlertChanged");
        var (hostId, agent) = await app.RegisterHostAsync(owner.Client, "live-c-1", "busy-1");
        await app.AddRuleAsync(owner.UserId, AlertMetric.CpuUsage, threshold: 80);

        await agent.SendSamplesAsync(
            MetricsIngestionTests.FullSample(app.Time.GetUtcNow().AddSeconds(-5)) with { Cpu = new CpuMetrics { UsagePercent = 99 } });
        await app.EvaluateAsync();

        var fired = await NextAsync(alerts, message => message.HostId == hostId);
        Assert.Equal(AlertEventKind.Fired, fired.Kind);
        Assert.Equal("CPU usage on busy-1 above 80%", fired.Title);
        Assert.Equal(AlertSeverity.Critical, fired.Severity);
    }

    [Fact]
    public async Task Hosts_going_offline_and_coming_back_are_announced()
    {
        await using var owner = await ConnectAsync("live-d@example.com");
        var statuses = Listen<LiveHostStatus>(owner.Connection, "HostStatus");
        var (hostId, agent) = await app.RegisterHostAsync(owner.Client, "live-d-1");
        var monitor = app.Factory.Services.GetRequiredService<HostStatusMonitor>();
        await monitor.CheckAsync(Ct);

        app.Time.Advance(TimeSpan.FromMinutes(3));
        await monitor.CheckAsync(Ct);
        Assert.Equal(HostStatus.Offline, (await NextAsync(statuses, message => message.HostId == hostId)).Status);

        await agent.SendSamplesAsync(MetricsIngestionTests.FullSample(app.Time.GetUtcNow()));
        await monitor.CheckAsync(Ct);
        Assert.Equal(HostStatus.Online, (await NextAsync(statuses, message => message.HostId == hostId)).Status);
    }

    [Fact]
    public async Task The_hub_needs_a_signed_in_user()
    {
        await using var anonymous = BuildConnection(cookie: null);

        await Assert.ThrowsAsync<HttpRequestException>(() => anonymous.StartAsync(Ct));
    }
}
