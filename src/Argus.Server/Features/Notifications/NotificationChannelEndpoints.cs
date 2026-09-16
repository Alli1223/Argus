using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Argus.Server.Data;
using Argus.Server.Features.Alerts;
using Argus.Server.Features.Auth;
using Argus.Server.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Argus.Server.Features.Notifications;

public sealed record NotificationChannelResponse(
    Guid Id,
    string Name,
    NotificationChannelKind Kind,
    string Target,
    AlertSeverity MinimumSeverity,
    bool NotifyOnResolved,
    bool Enabled,
    DeliverySummary? LastDelivery,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A channel's most recent notification: when it was queued, whether it went out, and why not.</summary>
public sealed record DeliverySummary(DeliveryStatus Status, int Attempts, DateTimeOffset CreatedAt, DateTimeOffset? SentAt, string? LastError);

/// <summary>Creates or replaces a channel.</summary>
public sealed record NotificationChannelRequest
{
    [Required, MaxLength(100)]
    public string Name { get; init; } = "";

    [Required]
    public NotificationChannelKind? Kind { get; init; }

    /// <summary>Email addresses separated by commas, or the webhook URL.</summary>
    [Required, MaxLength(2000)]
    public string Target { get; init; } = "";

    public AlertSeverity MinimumSeverity { get; init; } = AlertSeverity.Warning;

    public bool NotifyOnResolved { get; init; } = true;

    public bool Enabled { get; init; } = true;
}

/// <summary>What this server can send: email needs an SMTP server in its settings.</summary>
public sealed record NotificationSupport(bool Email);

/// <summary>Notification channels are personal: each user decides where their own alerts go.</summary>
public static class NotificationChannelEndpoints
{
    public const int MaxEmailAddresses = 10;

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    public static IEndpointRouteBuilder MapNotificationChannelEndpoints(this IEndpointRouteBuilder routes)
    {
        var channels = routes.MapGroup("/notification-channels").WithTags("Notification channels");

        channels.MapGet("/", ListAsync);
        channels.MapGet("/support", (IOptions<SmtpOptions> smtp) => new NotificationSupport(Email: smtp.Value.IsConfigured));
        channels.MapGet("/{id:guid}", GetAsync);
        channels.MapPost("/", CreateAsync);
        channels.MapPut("/{id:guid}", UpdateAsync);
        channels.MapDelete("/{id:guid}", DeleteAsync);
        channels.MapPost("/{id:guid}/test", SendTestAsync).RequireRateLimiting(RateLimiting.NotificationTestPolicy);

        return routes;
    }

    private static Task<List<NotificationChannelResponse>> ListAsync(ClaimsPrincipal user, ArgusDbContext db, CancellationToken cancellationToken)
    {
        var ownerId = user.GetUserId();
        return Project(db, db.NotificationChannels.Where(channel => channel.OwnerId == ownerId).OrderBy(channel => channel.Name))
            .ToListAsync(cancellationToken);
    }

    private static async Task<Results<Ok<NotificationChannelResponse>, NotFound>> GetAsync(
        Guid id, ClaimsPrincipal user, ArgusDbContext db, CancellationToken cancellationToken)
    {
        var ownerId = user.GetUserId();
        var channel = await Project(db, db.NotificationChannels.Where(c => c.Id == id && c.OwnerId == ownerId))
            .SingleOrDefaultAsync(cancellationToken);
        return channel is null ? TypedResults.NotFound() : TypedResults.Ok(channel);
    }

    private static async Task<Results<Created<NotificationChannelResponse>, ValidationProblem>> CreateAsync(
        NotificationChannelRequest request, ClaimsPrincipal user, ArgusDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var (errors, target) = Validate(request);
        if (errors is not null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var now = time.GetUtcNow();
        var channel = new NotificationChannel { OwnerId = user.GetUserId(), CreatedAt = now };
        Apply(channel, request, target, now);
        db.NotificationChannels.Add(channel);
        await db.SaveChangesAsync(cancellationToken);

        var created = await Project(db, db.NotificationChannels.Where(c => c.Id == channel.Id)).SingleAsync(cancellationToken);
        return TypedResults.Created($"/api/notification-channels/{channel.Id}", created);
    }

    private static async Task<Results<Ok<NotificationChannelResponse>, NotFound, ValidationProblem>> UpdateAsync(
        Guid id, NotificationChannelRequest request, ClaimsPrincipal user, ArgusDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var ownerId = user.GetUserId();
        var channel = await db.NotificationChannels.SingleOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId, cancellationToken);
        if (channel is null)
        {
            return TypedResults.NotFound();
        }

        var (errors, target) = Validate(request);
        if (errors is not null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // Queued notifications are sent to the channel as it is when their turn comes.
        Apply(channel, request, target, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(await Project(db, db.NotificationChannels.Where(c => c.Id == id)).SingleAsync(cancellationToken));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(
        Guid id, ClaimsPrincipal user, ArgusDbContext db, CancellationToken cancellationToken)
    {
        var ownerId = user.GetUserId();
        var deleted = await db.NotificationChannels
            .Where(channel => channel.Id == id && channel.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    /// <summary>Sends a test message straight away, even to a switched-off channel, and says whether it went out.</summary>
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> SendTestAsync(
        Guid id,
        ClaimsPrincipal user,
        ArgusDbContext db,
        IEnumerable<INotificationSender> senders,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        var ownerId = user.GetUserId();
        var channel = await db.NotificationChannels.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId, cancellationToken);
        if (channel is null)
        {
            return TypedResults.NotFound();
        }

        var test = new NotificationDelivery
        {
            ChannelId = channel.Id,
            Kind = NotificationKind.Test,
            Payload = "{}",
            CreatedAt = time.GetUtcNow(),
        };

        try
        {
            var sender = senders.FirstOrDefault(s => s.Kind == channel.Kind)
                ?? throw new NotSupportedException($"This server cannot send to {channel.Kind} channels.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TestTimeout);
            await sender.SendAsync(channel, test, timeout.Token);
            return TypedResults.NoContent();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            loggers.CreateLogger(typeof(NotificationChannelEndpoints).FullName!)
                .LogInformation(ex, "Test notification to channel {ChannelId} failed", channel.Id);
            var detail = ex is OperationCanceledException ? "It took too long to send." : ex.Message;
            return TypedResults.Problem(statusCode: StatusCodes.Status502BadGateway, title: "The test was not delivered", detail: detail);
        }
    }

    private static IQueryable<NotificationChannelResponse> Project(ArgusDbContext db, IQueryable<NotificationChannel> channels) =>
        channels.Select(channel => new NotificationChannelResponse(
            channel.Id,
            channel.Name,
            channel.Kind,
            channel.Target,
            channel.MinimumSeverity,
            channel.NotifyOnResolved,
            channel.Enabled,
            db.NotificationDeliveries
                .Where(delivery => delivery.ChannelId == channel.Id)
                .OrderByDescending(delivery => delivery.CreatedAt)
                .Select(delivery => new DeliverySummary(
                    delivery.Status, delivery.Attempts, delivery.CreatedAt, delivery.SentAt, delivery.LastError))
                .FirstOrDefault(),
            channel.CreatedAt,
            channel.UpdatedAt));

    private static (Dictionary<string, string[]>? Errors, string Target) Validate(NotificationChannelRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["Name the channel."];
        }

        var target = request.Target.Trim();
        if (request.Kind == NotificationChannelKind.Email)
        {
            var addresses = EmailAddresses.Split(target);
            if (addresses.Count == 0)
            {
                errors["target"] = ["Enter at least one email address."];
            }
            else if (addresses.Count > MaxEmailAddresses)
            {
                errors["target"] = [$"A channel can email up to {MaxEmailAddresses} addresses."];
            }
            else if (addresses.FirstOrDefault(address => !EmailAddresses.IsValid(address)) is { } invalid)
            {
                errors["target"] = [$"'{invalid}' is not an email address."];
            }
            else
            {
                target = string.Join(", ", addresses);
            }
        }
        else if (!Uri.TryCreate(target, UriKind.Absolute, out var url) || url.Scheme is not ("https" or "http"))
        {
            errors["target"] = ["Enter the whole webhook address, starting with https://."];
        }

        return (errors.Count == 0 ? null : errors, target);
    }

    private static void Apply(NotificationChannel channel, NotificationChannelRequest request, string target, DateTimeOffset now)
    {
        channel.Name = request.Name.Trim();
        channel.Kind = request.Kind!.Value;
        channel.Target = target;
        channel.MinimumSeverity = request.MinimumSeverity;
        channel.NotifyOnResolved = request.NotifyOnResolved;
        channel.Enabled = request.Enabled;
        channel.UpdatedAt = now;
    }
}
