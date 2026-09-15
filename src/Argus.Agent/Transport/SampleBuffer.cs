using Argus.Contracts.Agent;

namespace Argus.Agent.Transport;

/// <summary>
/// Samples waiting to be sent, oldest first. When the server is unreachable for longer than the buffer
/// covers, the oldest samples are dropped. Only the newest sample keeps its process list, which keeps
/// the buffer small and matches what the server stores (the latest snapshot).
/// </summary>
internal sealed class SampleBuffer(int capacity)
{
    private readonly LinkedList<MetricSample> _samples = new();
    private readonly Lock _lock = new();

    public int Capacity { get; } = capacity;

    /// <summary>Samples discarded because the buffer was full.</summary>
    public long Dropped { get; private set; }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _samples.Count;
            }
        }
    }

    public void Add(MetricSample sample)
    {
        lock (_lock)
        {
            if (_samples.Last is { Value.TopProcesses: not null } newest)
            {
                newest.Value = newest.Value with { TopProcesses = null };
            }

            while (_samples.Count >= Capacity)
            {
                _samples.RemoveFirst();
                Dropped++;
            }

            _samples.AddLast(sample);
        }
    }

    /// <summary>Up to <paramref name="maxCount"/> of the oldest samples, without removing them.</summary>
    public List<MetricSample> PeekBatch(int maxCount)
    {
        lock (_lock)
        {
            return _samples.Take(maxCount).ToList();
        }
    }

    /// <summary>Removes the <paramref name="count"/> oldest samples once they have been delivered.</summary>
    public void Remove(int count)
    {
        lock (_lock)
        {
            for (var i = 0; i < count && _samples.First is not null; i++)
            {
                _samples.RemoveFirst();
            }
        }
    }
}
