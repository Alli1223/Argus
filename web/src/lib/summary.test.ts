import { describe, expect, it } from "vitest";
import type { UsageSummary } from "../api/types";
import { stats } from "../test/usage";
import { combineStats, hostUsage, periodLabel, periodOption, sortHostUsage } from "./summary";

describe("periodOption", () => {
  it("reads the grouping from the address, defaulting to days", () => {
    expect(periodOption("month").value).toBe("Month");
    expect(periodOption("YEAR").value).toBe("Year");
    expect(periodOption(null).value).toBe("Day");
    expect(periodOption("week").value).toBe("Day");
  });
});

describe("periodLabel", () => {
  it("names the newest two periods and dates the rest in the summary's time zone", () => {
    expect(periodLabel("Day", 0, "2026-11-11T00:00:00Z", "UTC")).toBe("Today");
    expect(periodLabel("Month", 1, "2026-10-01T00:00:00Z", "UTC")).toBe("Last month");
    expect(periodLabel("Year", 2, "2024-01-01T00:00:00Z", "UTC")).toBe("2024");
    // Auckland's November starts on the last day of October in UTC.
    expect(periodLabel("Month", 2, "2026-10-31T11:00:00Z", "Pacific/Auckland")).toMatch(/November/);
  });
});

describe("combineStats", () => {
  it("adds bytes and alerts, keeps peaks and weights averages by reporting time", () => {
    const combined = combineStats([
      stats({
        networkReceivedBytes: 100,
        cpuAveragePercent: 10,
        cpuPeakPercent: 40,
        reportingSeconds: 300,
        alerts: 1,
      }),
      stats({
        networkReceivedBytes: 50,
        cpuAveragePercent: 50,
        cpuPeakPercent: 90,
        reportingSeconds: 100,
        criticalAlerts: 1,
        alerts: 2,
      }),
      stats(),
    ]);

    expect(combined.networkReceivedBytes).toBe(150);
    expect(combined.diskReadBytes).toBeNull();
    expect(combined.cpuAveragePercent).toBe(20);
    expect(combined.cpuPeakPercent).toBe(90);
    expect(combined.reportingSeconds).toBe(400);
    expect(combined.alerts).toBe(3);
    expect(combined.criticalAlerts).toBe(1);
  });
});

const summary: UsageSummary = {
  period: "Day",
  timeZone: "UTC",
  historyDays: 730,
  hosts: [
    { hostId: "a", displayName: "alpha" },
    { hostId: "b", displayName: "beta" },
    { hostId: "c", displayName: "gamma" },
  ],
  periods: [
    {
      start: "2026-11-11T00:00:00Z",
      end: "2026-11-12T00:00:00Z",
      totals: stats(),
      hosts: [{ hostId: "a", stats: stats({ networkReceivedBytes: 10 }) }],
    },
    {
      start: "2026-11-10T00:00:00Z",
      end: "2026-11-11T00:00:00Z",
      totals: stats(),
      hosts: [
        { hostId: "a", stats: stats({ networkReceivedBytes: 5 }) },
        { hostId: "b", stats: stats({ networkReceivedBytes: 100 }) },
      ],
    },
  ],
};

describe("hostUsage", () => {
  it("gives every host a row for one period or all of them", () => {
    expect(hostUsage(summary, 0).map((row) => row.stats?.networkReceivedBytes ?? null)).toEqual([
      10,
      null,
      null,
    ]);
    expect(hostUsage(summary, null).map((row) => row.stats?.networkReceivedBytes ?? null)).toEqual([
      15,
      100,
      null,
    ]);
  });
});

describe("sortHostUsage", () => {
  it("keeps hosts without figures last in either direction", () => {
    const rows = hostUsage(summary, null);
    expect(sortHostUsage(rows, "received", true).map((row) => row.displayName)).toEqual([
      "beta",
      "alpha",
      "gamma",
    ]);
    expect(sortHostUsage(rows, "received", false).map((row) => row.displayName)).toEqual([
      "alpha",
      "beta",
      "gamma",
    ]);
    expect(sortHostUsage(rows, "name", true).map((row) => row.displayName)).toEqual([
      "gamma",
      "beta",
      "alpha",
    ]);
  });
});
