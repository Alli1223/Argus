using System.ComponentModel.DataAnnotations;

namespace Argus.Server.Data;

public sealed class DatabaseOptions
{
    public const string SectionName = "Argus:Database";

    /// <summary>Apply pending EF Core migrations when the server starts.</summary>
    public bool MigrateOnStartup { get; set; } = true;

    /// <summary>How many times to try reaching the database at startup (2 seconds apart).</summary>
    [Range(1, 1000)]
    public int StartupRetries { get; set; } = 30;
}
