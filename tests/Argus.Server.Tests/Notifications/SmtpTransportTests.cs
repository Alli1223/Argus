using System.Text.Json;
using Argus.Server.Features.Notifications;
using Argus.Server.Features.Settings;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MimeKit;

namespace Argus.Server.Tests.Notifications;

/// <summary>Sends real mail through the SMTP transport to a Mailpit container, then reads it back from Mailpit's API.</summary>
public sealed class SmtpTransportTests : IAsyncLifetime
{
    private const int SmtpPort = 1025;
    private const int ApiPort = 8025;

    private readonly IContainer _mailpit = new ContainerBuilder("axllent/mailpit:v1.31.1")
        .WithPortBinding(SmtpPort, assignRandomHostPort: true)
        .WithPortBinding(ApiPort, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(ApiPort).ForPath("/api/v1/info")))
        .Build();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _mailpit.StartAsync(Ct);

    public async ValueTask DisposeAsync() => await _mailpit.DisposeAsync();

    [Fact]
    public async Task Emails_reach_the_mail_server()
    {
        var options = new SmtpOptions
        {
            Host = _mailpit.Hostname,
            Port = _mailpit.GetMappedPublicPort(SmtpPort),
            Security = SmtpSecurity.None,
            From = "argus@argus.test",
        };
        var message = new MimeMessage { Subject = "[Critical] CPU usage on web-1 above 90%" };
        message.From.Add(new MailboxAddress("Argus", "argus@argus.test"));
        message.To.Add(MailboxAddress.Parse("ops@example.com"));
        message.Body = new BodyBuilder { TextBody = "Reading: 97%", HtmlBody = "<p>Reading: 97%</p>" }.ToMessageBody();

        await new SmtpEmailTransport(new FixedEmailSettings(options)).SendAsync(message, Ct);

        using var api = new HttpClient { BaseAddress = new Uri($"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(ApiPort)}") };
        using var inbox = JsonDocument.Parse(await api.GetStringAsync("/api/v1/messages", Ct));
        var received = Assert.Single(inbox.RootElement.GetProperty("messages").EnumerateArray());
        Assert.Equal(message.Subject, received.GetProperty("Subject").GetString());
        Assert.Equal("ops@example.com", received.GetProperty("To")[0].GetProperty("Address").GetString());
    }
}

/// <summary>The settings a test sends with, in place of the store that reads them from the database.</summary>
file sealed class FixedEmailSettings(SmtpOptions smtp) : IEmailSettings
{
    public ValueTask<SmtpOptions> CurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult(smtp);
}
