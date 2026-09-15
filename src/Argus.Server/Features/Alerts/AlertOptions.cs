using System.ComponentModel.DataAnnotations;

namespace Argus.Server.Features.Alerts;

public sealed class AlertOptions
{
    public const string SectionName = "Argus:Alerts";

    /// <summary>How often alert rules are evaluated.</summary>
    [Range(5, 3600)]
    public int EvaluationIntervalSeconds { get; set; } = 30;

    /// <summary>Evaluate rules in the background (tests switch this off and evaluate on demand).</summary>
    public bool BackgroundEvaluation { get; set; } = true;
}
