using Argus.Server.Infrastructure;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Argus.Server.Features.Notifications;

/// <summary>Sends deliveries to one kind of channel. An exception counts as a failed attempt.</summary>
public interface INotificationSender
{
    NotificationChannelKind Kind { get; }

    Task SendAsync(NotificationChannel channel, NotificationDelivery delivery, CancellationToken cancellationToken);
}

/// <summary>Hands finished emails to a mail server. Tests swap in a fake.</summary>
public interface IEmailTransport
{
    Task SendAsync(MimeMessage message, CancellationToken cancellationToken);
}

/// <summary>The addresses in an email channel's target: separated by commas, semicolons or spaces.</summary>
public static class EmailAddresses
{
    public static IReadOnlyList<string> Split(string target) =>
        target.Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

internal sealed class EmailNotificationSender(IEmailTransport transport, IOptions<SmtpOptions> smtp, IOptions<ArgusOptions> argus)
    : INotificationSender
{
    public NotificationChannelKind Kind => NotificationChannelKind.Email;

    public async Task SendAsync(NotificationChannel channel, NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        var options = smtp.Value;
        if (!options.IsConfigured)
        {
            throw new InvalidOperationException("Email is not set up on this server: set Argus:Smtp:Host and Argus:Smtp:From.");
        }

        var content = delivery.Kind switch
        {
            NotificationKind.AlertFired or NotificationKind.AlertResolved =>
                AlertEmail.Render(AlertNotification.FromJson(delivery.Payload), channel.Name, argus.Value.PublicUrl),
            _ => throw new NotSupportedException($"There is no email for {delivery.Kind} notifications."),
        };

        var message = new MimeMessage { Subject = content.Subject };
        message.From.Add(new MailboxAddress(options.FromName, options.From));
        message.To.AddRange(EmailAddresses.Split(channel.Target).Select(MailboxAddress.Parse));
        message.Body = new BodyBuilder { TextBody = content.Text, HtmlBody = content.Html }.ToMessageBody();

        await transport.SendAsync(message, cancellationToken);
    }
}

internal sealed class SmtpEmailTransport(IOptions<SmtpOptions> options) : IEmailTransport
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task SendAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        var smtp = options.Value;
        if (!smtp.IsConfigured)
        {
            throw new InvalidOperationException("Email is not set up on this server: set Argus:Smtp:Host and Argus:Smtp:From.");
        }

        using var client = new MailKit.Net.Smtp.SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };

        var security = smtp.Security switch
        {
            SmtpSecurity.None => SecureSocketOptions.None,
            SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
            SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
            _ => SecureSocketOptions.Auto,
        };
        await client.ConnectAsync(smtp.Host, smtp.Port, security, cancellationToken);

        if (!string.IsNullOrEmpty(smtp.Username))
        {
            await client.AuthenticateAsync(smtp.Username, smtp.Password ?? "", cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
