using Argus.Agent.Transport;
using Argus.Contracts.Agent;

namespace Argus.Agent.Tests.Transport;

public class SampleBufferTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);

    internal static MetricSample Sample(int second, bool withProcesses = false) => new()
    {
        Timestamp = Start.AddSeconds(second),
        Cpu = new CpuMetrics { UsagePercent = second },
        Memory = new MemoryMetrics { TotalBytes = 100, UsedBytes = 50, AvailableBytes = 50 },
        TopProcesses = withProcesses ? [new ProcessMetrics { Pid = 1, Name = "init" }] : null,
    };

    private static IEnumerable<int> Seconds(IEnumerable<MetricSample> samples) => samples.Select(s => (int)s.Cpu.UsagePercent);

    [Fact]
    public void The_oldest_samples_are_dropped_when_full()
    {
        var buffer = new SampleBuffer(capacity: 3);

        for (var i = 0; i < 5; i++)
        {
            buffer.Add(Sample(i));
        }

        Assert.Equal(3, buffer.Count);
        Assert.Equal(2, buffer.Dropped);
        Assert.Equal([2, 3, 4], Seconds(buffer.PeekBatch(10)));
    }

    [Fact]
    public void Batches_come_oldest_first_and_leave_once_delivered()
    {
        var buffer = new SampleBuffer(capacity: 100);
        for (var i = 0; i < 5; i++)
        {
            buffer.Add(Sample(i));
        }

        var batch = buffer.PeekBatch(2);

        Assert.Equal([0, 1], Seconds(batch));
        Assert.Equal(5, buffer.Count);

        buffer.Remove(batch.Count);
        Assert.Equal([2, 3, 4], Seconds(buffer.PeekBatch(10)));

        buffer.Remove(10);
        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public void Only_the_newest_sample_keeps_its_process_list()
    {
        var buffer = new SampleBuffer(capacity: 10);

        buffer.Add(Sample(0, withProcesses: true));
        buffer.Add(Sample(1, withProcesses: true));

        var samples = buffer.PeekBatch(10);
        Assert.Null(samples[0].TopProcesses);
        Assert.NotNull(samples[1].TopProcesses);
    }
}
