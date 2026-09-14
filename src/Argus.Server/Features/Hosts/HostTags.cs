using System.Diagnostics.CodeAnalysis;

namespace Argus.Server.Features.Hosts;

/// <summary>Tags group hosts ("prod", "web", "eu-west"). They are compared case-insensitively.</summary>
public static class HostTags
{
    public const int MaxTags = 20;
    public const int MaxLength = 50;

    /// <summary>Trims, lower-cases and de-duplicates tags, or explains why they cannot be used.</summary>
    public static bool TryNormalize(
        IEnumerable<string> tags, out List<string> normalized, [NotNullWhen(false)] out string? error)
    {
        normalized = [];
        foreach (var raw in tags)
        {
            var tag = raw.Trim().ToLowerInvariant();
            if (tag.Length is 0 or > MaxLength || tag.Any(c => char.IsWhiteSpace(c) || c == ','))
            {
                error = $"Tags must be 1-{MaxLength} characters without spaces or commas.";
                return false;
            }

            if (!normalized.Contains(tag))
            {
                normalized.Add(tag);
            }
        }

        if (normalized.Count > MaxTags)
        {
            error = $"At most {MaxTags} tags are allowed.";
            return false;
        }

        error = null;
        return true;
    }
}
