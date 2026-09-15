using Argus.Server.Features.Metrics;

namespace Argus.Server.Tests.Metrics;

public class SeriesResolutionTests
{
    private static readonly TimeSpan CollectionInterval = TimeSpan.FromSeconds(15);

    [Theory]
    [InlineData(1, "Raw", 30)]
    [InlineData(6, "Raw", 120)]
    [InlineData(24, "FiveMinutes", 300)]
    [InlineData(24 * 7, "FiveMinutes", 3600)]
    [InlineData(24 * 30, "Hourly", 3 * 3600)]
    [InlineData(24 * 365, "Hourly", 2 * 86400)]
    public void The_source_and_bucket_follow_the_range(int hours, string source, int bucketSeconds)
    {
        var (chosen, bucket) = SeriesResolution.Choose(TimeSpan.FromHours(hours), 300, CollectionInterval);

        Assert.Equal(source, chosen.ToString());
        Assert.Equal(bucketSeconds, bucket.TotalSeconds);
    }

    [Theory]
    [InlineData(24, "Raw", 300)]
    [InlineData(24 * 3, "Hourly", 3600)]
    public void Filesystem_and_network_series_use_raw_or_hourly_data(int hours, string source, int bucketSeconds)
    {
        var (chosen, bucket) = SeriesResolution.ChooseRawOrHourly(TimeSpan.FromHours(hours), 300, CollectionInterval);

        Assert.Equal(source, chosen.ToString());
        Assert.Equal(bucketSeconds, bucket.TotalSeconds);
    }

    [Fact]
    public void Ranges_default_to_the_last_hour()
    {
        var now = new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

        var errors = SeriesRange.TryCreate(null, null, null, now, out var range);

        Assert.Null(errors);
        Assert.Equal(now.AddHours(-1), range.From);
        Assert.Equal(now, range.To);
        Assert.Equal(300, range.Points);
    }

    [Fact]
    public void Ranges_longer_than_two_years_are_refused()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.NotNull(SeriesRange.TryCreate(now.AddDays(-800), now, 300, now, out _));
    }
}
