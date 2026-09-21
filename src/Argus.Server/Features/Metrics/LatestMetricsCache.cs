using System.Collections.Concurrent;
using Argus.Server.Features.Hosts;

namespace Argus.Server.Features.Metrics;

/// <summary>
/// In-memory store of each host's most recently ingested metrics. Updated on every ingest so that
/// the hosts list reads from memory rather than the database, making it near-instant.
/// </summary>
public sealed class LatestMetricsCache
{
    private readonly ConcurrentDictionary<Guid, LatestMetrics> _cache = new();

    public void Set(Guid hostId, LatestMetrics metrics) => _cache[hostId] = metrics;

    /// <summary>Returns cached metrics for each id that has been seen since startup.</summary>
    public Dictionary<Guid, LatestMetrics> Get(IReadOnlyCollection<Guid> hostIds)
    {
        var result = new Dictionary<Guid, LatestMetrics>(hostIds.Count);
        foreach (var id in hostIds)
        {
            if (_cache.TryGetValue(id, out var m))
                result[id] = m;
        }
        return result;
    }

    public LatestMetrics? Get(Guid hostId) => _cache.TryGetValue(hostId, out var m) ? m : null;
}
