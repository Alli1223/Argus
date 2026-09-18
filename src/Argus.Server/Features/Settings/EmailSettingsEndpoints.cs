using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Argus.Server.Features.Auth;
using Argus.Server.Features.Notifications;
using Argus.Server.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using MimeKit;

namespace Argus.Server.Features.Settings;

/// <summary>The mail server Argus sends through. The password is never sent back, only whether there is one.</summary>
public sealed record EmailSettingsResponse(
    bool Configured,
    EmailSettingsSource Source,
    string? Host,
    int Port,
    SmtpSecurity Security,
    string? Username,
    bool HasPassword,
    string? From,
    string FromName,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy);

/// <summary>New mail server settings. Leave <see cref="Password"/> out to keep the saved one.</summary>
public sealed record EmailSettingsRequest
{
    [Required, MaxLength(255)]
    public string Host { get; init; } = "";

    public int Port { get; init; } = 587;

    public SmtpSecurity Security { get; init; } = SmtpSecurity.Auto;

    [MaxLength(255)]
    public string? Username { get; init; }

    /// <summary>The sign-in password, or an app password. Null keeps the saved one; empty clears it.</summary>
    [MaxLength(255)]
    public string? Password { get; init; }

    [Required, MaxLength(255)]
    public string From { get; init; } = "";

    [MaxLength(100)]
    public string FromName { get; init; } = "Argus";
}

/// <summary>Where to send a test email.</summary>
public sealed record EmailTestRequest([property: Required, MaxLength(255)] string To);

/// <summary>
/// Server-wide settings administrators change in the web app. Saved settings replace what the Compose
/// file sets, so a server can be set up without editing files on the machine it runs on.
/// </summary>
public static class EmailSettingsEndpoints
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder routes)
    {
        var settings = routes.MapGroup("/settings")
            .WithTags("Settings")
            .RequireAuthorization(policy => policy.RequireRole(Roles.Admin));

        settings.MapGet("/email", DescribeAsync);
        settings.MapPut("/email", SaveAsync);
        settings.MapDelete("/email", ClearAsync);
        settings.MapPost("/email/test", SendTestAsync).RequireRateLimiting(RateLimiting.NotificationTestPolicy);

        return routes;
    }

    private static async Task<Ok<EmailSettingsResponse>> DescribeAsync(EmailSettingsStore store, CancellationToken cancellationToken) =>
        TypedResults.Ok(Describe(await store.DescribeAsync(cancellationToken)));

    private static async Task<Results<Ok<EmailSettingsResponse>, ValidationProblem>> SaveAsync(
        EmailSettingsRequest request,
        ClaimsPrincipal user,
        EmailSettingsStore store,
        CancellationToken cancellationToken)
    {
        if (Validate(request) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var input = new EmailSettingsInput(
            request.Host, request.Port, request.Security, request.Username, request.Password, request.From, request.FromName);
        await store.SaveAsync(input, user.Identity?.Name, cancellationToken);
        return TypedResults.Ok(Describe(await store.DescribeAsync(cancellationToken)));
    }

    /// <summary>Forgets the settings saved here, so the ones in the Compose file are used again.</summary>
    private static async Task<NoContent> ClearAsync(EmailSettingsStore store, CancellationToken cancellationToken)
    {
        await store.ClearAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>Sends a test email with the settings in use, and says what the mail server answered.</summary>
    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> SendTestAsync(
        EmailTestRequest request,
        EmailSettingsStore store,
        IEmailTransport transport,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        var to = (request.To ?? "").Trim();
        if (!EmailAddresses.IsValid(to))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["to"] = [$"'{to}' is not an email address."],
            });
        }

        var smtp = await store.CurrentAsync(cancellationToken);
        if (!smtp.IsConfigured)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Email is not set up yet",
                detail: "Save a mail server and the address emails come from, then send a test.");
        }

        var content = NotificationEmail.ForServerTest();
        var message = new MimeMessage { Subject = content.Subject };
        message.From.Add(new MailboxAddress(smtp.FromName, smtp.From));
        message.To.Add(MailboxAddress.Parse(to));
        message.Body = new BodyBuilder { TextBody = content.Text, HtmlBody = content.Html }.ToMessageBody();

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TestTimeout);
            await transport.SendAsync(message, timeout.Token);
            return TypedResults.NoContent();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            loggers.CreateLogger(typeof(EmailSettingsEndpoints).FullName!)
                .LogInformation(ex, "The test email to {Address} was not sent", to);
            var detail = ex is OperationCanceledException ? "The mail server took too long to answer." : ex.Message;
            return TypedResults.Problem(statusCode: StatusCodes.Status502BadGateway, title: "The test was not sent", detail: detail);
        }
    }

    private static EmailSettingsResponse Describe(EmailSettingsState state) =>
        new(state.Smtp.IsConfigured,
            state.Source,
            state.Smtp.Host,
            state.Smtp.Port,
            state.Smtp.Security,
            state.Smtp.Username,
            state.HasPassword,
            state.Smtp.From,
            state.Smtp.FromName,
            state.UpdatedAt,
            state.UpdatedBy);

    private static Dictionary<string, string[]>? Validate(EmailSettingsRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        var host = request.Host.Trim();
        if (host.Length == 0)
        {
            errors["host"] = ["Enter the mail server, such as smtp.gmail.com."];
        }
        else if (host.Any(character => char.IsWhiteSpace(character) || character is '/' or '@'))
        {
            // A whole URL, an address or a host with a path: the name on its own is what MailKit connects to.
            errors["host"] = ["Enter the mail server's name on its own, such as smtp.gmail.com."];
        }

        if (request.Port is < 1 or > 65535)
        {
            errors["port"] = ["The port is a number between 1 and 65535, usually 587."];
        }

        var from = request.From.Trim();
        if (from.Length == 0)
        {
            errors["from"] = ["Enter the address emails come from."];
        }
        else if (!EmailAddresses.IsValid(from))
        {
            errors["from"] = [$"'{from}' is not an email address."];
        }

        return errors.Count == 0 ? null : errors;
    }
}
