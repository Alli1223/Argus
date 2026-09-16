using Argus.Server.Data;
using Argus.Server.Features.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;

namespace Argus.Server.Tests.Infrastructure;

/// <summary>An alerts server that emails through a fake mail server, delivering on demand.</summary>
public sealed class NotificationsFixture(PostgresFixture postgres) : AlertsFixture(postgres)
{
    public const string PublicUrl = "https://argus.test";

    public FakeEmailTransport Email { get; } = new();

    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["Argus:PublicUrl"] = PublicUrl,
        ["Argus:Notifications:BackgroundDelivery"] = "false",
        ["Argus:Notifications:MaxAttempts"] = "3",
        ["Argus:Smtp:Host"] = "smtp.argus.test",
        ["Argus:Smtp:From"] = "argus@argus.test",
    };

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddSingleton<IEmailTransport>(Email);
    }

    public Task<DispatchResult> DispatchAsync() =>
        WithScopeAsync(services => services.GetRequiredService<NotificationDispatcher>().DispatchDueAsync(TestContext.Current.CancellationToken));

    public Task<NotificationChannel> AddEmailChannelAsync(Guid ownerId, string addresses, Action<NotificationChannel>? configure = null) =>
        WithScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ArgusDbContext>();
            var channel = new NotificationChannel
            {
                OwnerId = ownerId,
                Name = addresses,
                Kind = NotificationChannelKind.Email,
                Target = addresses,
                CreatedAt = Time.GetUtcNow(),
                UpdatedAt = Time.GetUtcNow(),
            };
            configure?.Invoke(channel);
            db.NotificationChannels.Add(channel);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return channel;
        });

    public Task<List<NotificationDelivery>> DeliveriesAsync(Guid channelId) =>
        WithScopeAsync(services => services.GetRequiredService<ArgusDbContext>().NotificationDeliveries
            .AsNoTracking()
            .Where(delivery => delivery.ChannelId == channelId)
            .OrderBy(delivery => delivery.CreatedAt)
            .ToListAsync(TestContext.Current.CancellationToken));
}

/// <summary>Keeps emails instead of sending them, or fails every send while <see cref="FailWith"/> is set.</summary>
public sealed class FakeEmailTransport : IEmailTransport
{
    private readonly List<MimeMessage> _sent = [];

    public string? FailWith { get; set; }

    public IReadOnlyList<MimeMessage> SentTo(string address)
    {
        lock (_sent)
        {
            return [.. _sent.Where(message => message.To.Mailboxes.Any(mailbox => mailbox.Address == address))];
        }
    }

    public Task SendAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        if (FailWith is { } error)
        {
            throw new IOException(error);
        }

        lock (_sent)
        {
            _sent.Add(message);
        }

        return Task.CompletedTask;
    }
}
