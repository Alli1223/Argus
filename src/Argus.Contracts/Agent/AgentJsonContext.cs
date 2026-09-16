using System.Text.Json;
using System.Text.Json.Serialization;

namespace Argus.Contracts.Agent;

/// <summary>
/// Source-generated serializer metadata for the agent protocol. Used by both sides so the wire
/// format (camelCase, string enums, nulls omitted) is identical and trimming-safe.
/// </summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RegisterAgentRequest))]
[JsonSerializable(typeof(RegisterAgentResponse))]
[JsonSerializable(typeof(InventoryReport))]
[JsonSerializable(typeof(AgentSettings))]
[JsonSerializable(typeof(MetricsBatch))]
[JsonSerializable(typeof(MetricsBatchResponse))]
[JsonSerializable(typeof(AgentUpdateOffer))]
[JsonSerializable(typeof(AgentUpdateResult))]
public sealed partial class AgentJsonContext : JsonSerializerContext;
