using System.ComponentModel.DataAnnotations;

namespace Argus.Server.Infrastructure;

/// <summary>General server settings, bound from the <c>Argus</c> configuration section.</summary>
public sealed class ArgusOptions
{
    public const string SectionName = "Argus";

    /// <summary>
    /// External base URL that browsers and agents use to reach the server
    /// (e.g. <c>https://argus.example.com</c>). Used in install commands and emails.
    /// </summary>
    [Url]
    public string? PublicUrl { get; set; }
}
