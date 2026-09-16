using System.ComponentModel.DataAnnotations;

namespace Argus.Server.Features.Notifications;

public enum ReportKind
{
    Daily,
    Weekly,
}

public sealed class ReportOptions
{
    public const string SectionName = "Argus:Reports";

    /// <summary>Queue reports in the background (tests switch this off and queue them on demand).</summary>
    public bool BackgroundScheduling { get; set; } = true;

    /// <summary>The hour (UTC) when reports go out. Each report covers the day or week up to that hour.</summary>
    [Range(0, 23)]
    public int SendHourUtc { get; set; } = 7;

    /// <summary>The day weekly reports go out.</summary>
    public DayOfWeek WeeklyDay { get; set; } = DayOfWeek.Monday;
}

public static class ReportSchedule
{
    public static TimeSpan Length(ReportKind kind) => kind == ReportKind.Daily ? TimeSpan.FromDays(1) : TimeSpan.FromDays(7);

    /// <summary>The end of the latest report period that has finished by <paramref name="now"/>.</summary>
    public static DateTimeOffset LatestDue(ReportKind kind, DateTimeOffset now, ReportOptions options)
    {
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddHours(options.SendHourUtc);
        var daily = today <= now ? today : today.AddDays(-1);
        if (kind == ReportKind.Daily)
        {
            return daily;
        }

        var daysSinceWeeklyDay = ((int)daily.DayOfWeek - (int)options.WeeklyDay + 7) % 7;
        return daily.AddDays(-daysSinceWeeklyDay);
    }
}
