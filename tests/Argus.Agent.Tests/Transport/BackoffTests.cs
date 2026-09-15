using Argus.Agent.Transport;

namespace Argus.Agent.Tests.Transport;

public class BackoffTests
{
    [Fact]
    public void Delays_double_until_they_reach_the_cap()
    {
        var backoff = new Backoff(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5));

        var delays = Enumerable.Range(0, 12).Select(_ => backoff.NextDelay()).ToList();

        Assert.InRange(delays[0].TotalSeconds, 4, 6);
        Assert.InRange(delays[1].TotalSeconds, 8, 12);
        Assert.InRange(delays[2].TotalSeconds, 16, 24);
        Assert.All(delays, delay => Assert.True(delay <= TimeSpan.FromMinutes(5)));
        Assert.InRange(delays[^1].TotalSeconds, 240, 300);
        Assert.Equal(12, backoff.Failures);
    }

    [Fact]
    public void Reset_starts_over()
    {
        var backoff = new Backoff(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5));
        for (var i = 0; i < 6; i++)
        {
            backoff.NextDelay();
        }

        backoff.Reset();

        Assert.InRange(backoff.NextDelay().TotalSeconds, 4, 6);
    }
}
