using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MimeKit;

namespace Argus.Server.Features.Notifications;

public enum SmtpSecurity
{
    /// <summary>TLS on port 465, otherwise STARTTLS when the server offers it.</summary>
    Auto,

    None,

    StartTls,

    SslOnConnect,
}

/// <summary>The mail server email notifications go through. Without a host, no emails are sent.</summary>
public sealed class SmtpOptions : IValidatableObject
{
    public const string SectionName = "Argus:Smtp";

    public string? Host { get; set; }

    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    public SmtpSecurity Security { get; set; } = SmtpSecurity.Auto;

    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>The address emails come from, such as <c>argus@example.com</c>.</summary>
    public string? From { get; set; }

    public string FromName { get; set; } = "Argus";

    [MemberNotNullWhen(true, nameof(Host), nameof(From))]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(From) && !MailboxAddress.TryParse(From, out _))
        {
            yield return new ValidationResult($"'{From}' is not an email address.", [nameof(From)]);
        }
        else if (!string.IsNullOrWhiteSpace(Host) && string.IsNullOrWhiteSpace(From))
        {
            yield return new ValidationResult("Set the address emails come from as well as the mail server.", [nameof(From)]);
        }
    }
}
