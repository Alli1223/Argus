using System.ComponentModel.DataAnnotations;

namespace Argus.Server.Features.Updates;

public sealed class UpdateOptions
{
    public const string SectionName = "Argus:Updates";

    /// <summary>Look for new releases. Switched off, there are no update notices and agents cannot be updated.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Check on a timer (tests switch this off and check on demand).</summary>
    public bool BackgroundChecks { get; set; } = true;

    /// <summary>The GitHub repository releases come from, as owner/name.</summary>
    [RegularExpression("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", ErrorMessage = "Give the repository as owner/name.")]
    public string Repository { get; set; } = "Alli1223/Argus";

    [Range(1, 168)]
    public int CheckIntervalHours { get; set; } = 6;

    /// <summary>The address of GitHub's API.</summary>
    [Url]
    public string ApiUrl { get; set; } = "https://api.github.com";

    /// <summary>Where agent builds downloaded for updates are kept; a temporary folder when empty.</summary>
    public string? CacheDirectory { get; set; }
}
