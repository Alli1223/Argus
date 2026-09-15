using System.ComponentModel.DataAnnotations;
using Argus.Contracts.Agent;

namespace Argus.Server.Features.Agents;

/// <summary>Settings pushed to agents, bound from <c>Argus:Agents</c>.</summary>
public sealed class AgentOptions
{
    public const string SectionName = "Argus:Agents";

    [Range(5, 3600)]
    public int CollectionIntervalSeconds { get; set; } = 15;

    [Range(5, 1440)]
    public int InventoryIntervalMinutes { get; set; } = 60;

    [Range(0, AgentLimits.MaxTopProcesses)]
    public int TopProcessCount { get; set; } = 10;

    /// <summary>A host counts as offline once it has not reported for this long.</summary>
    [Range(15, 86_400)]
    public int OfflineAfterSeconds { get; set; } = 90;

    public AgentSettings ToAgentSettings() => new()
    {
        CollectionIntervalSeconds = CollectionIntervalSeconds,
        InventoryIntervalMinutes = InventoryIntervalMinutes,
        TopProcessCount = TopProcessCount,
    };
}
