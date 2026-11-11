import type { SummaryPeriod, UsagePeriod, UsageStats, UsageSummary } from "../api/types";

export interface PeriodOption {
  value: SummaryPeriod;
  label: string;
  /** How many periods to ask for. */
  count: number;
  /** "the last 30 days", for the totals row. */
  span: string;
}

export const PERIOD_OPTIONS: readonly PeriodOption[] = [
  { value: "Day", label: "Daily", count: 30, span: "the last 30 days" },
  { value: "Month", label: "Monthly", count: 12, span: "the last 12 months" },
  { value: "Year", label: "Yearly", count: 3, span: "the last 3 years" },
];

export function periodOption(value: string | null): PeriodOption {
  return (
    PERIOD_OPTIONS.find((option) => option.value.toLowerCase() === value?.toLowerCase()) ?? PERIOD_OPTIONS[0]
  );
}

const RECENT: Record<SummaryPeriod, [string, string]> = {
  Day: ["Today", "Yesterday"],
  Month: ["This month", "Last month"],
  Year: ["This year", "Last year"],
};

/** The period's date in its time zone: "Tue 4 Nov 2026", "November 2026", "2026". */
export function periodDate(period: SummaryPeriod, start: string, timeZone: string): string {
  const options: Intl.DateTimeFormatOptions =
    period === "Day"
      ? { weekday: "short", day: "numeric", month: "short", year: "numeric" }
      : period === "Month"
        ? { month: "long", year: "numeric" }
        : { year: "numeric" };
  return new Intl.DateTimeFormat(undefined, { ...options, timeZone }).format(new Date(start));
}

/** "Today" and "Yesterday" (or this and last month or year) for the newest two, else the date. */
export function periodLabel(period: SummaryPeriod, index: number, start: string, timeZone: string): string {
  return index < 2 ? RECENT[period][index] : periodDate(period, start, timeZone);
}

/**
 * Adds stats up: byte counts, reporting time and alerts add; peaks take the highest; CPU and memory
 * averages are weighted by how long each part reported, availability is the plain average. Figures
 * nothing had stay null.
 */
export function combineStats(parts: UsageStats[]): UsageStats {
  const sum = (pick: (stats: UsageStats) => number | null) => {
    const values = parts.map(pick).filter((value): value is number => value != null);
    return values.length === 0 ? null : values.reduce((total, value) => total + value, 0);
  };
  const max = (pick: (stats: UsageStats) => number | null) => {
    const values = parts.map(pick).filter((value): value is number => value != null);
    return values.length === 0 ? null : Math.max(...values);
  };
  const weighted = (pick: (stats: UsageStats) => number | null) => {
    const values = parts.filter((stats) => pick(stats) != null && (stats.reportingSeconds ?? 0) > 0);
    const seconds = values.reduce((total, stats) => total + stats.reportingSeconds!, 0);
    return seconds === 0
      ? null
      : values.reduce((total, stats) => total + pick(stats)! * stats.reportingSeconds!, 0) / seconds;
  };

  const mean = (pick: (stats: UsageStats) => number | null) => {
    const values = parts.map(pick).filter((value): value is number => value != null);
    return values.length === 0 ? null : values.reduce((total, value) => total + value, 0) / values.length;
  };

  return {
    diskReadBytes: sum((stats) => stats.diskReadBytes),
    diskWriteBytes: sum((stats) => stats.diskWriteBytes),
    networkReceivedBytes: sum((stats) => stats.networkReceivedBytes),
    networkSentBytes: sum((stats) => stats.networkSentBytes),
    cpuAveragePercent: weighted((stats) => stats.cpuAveragePercent),
    cpuPeakPercent: max((stats) => stats.cpuPeakPercent),
    memoryAveragePercent: weighted((stats) => stats.memoryAveragePercent),
    memoryPeakPercent: max((stats) => stats.memoryPeakPercent),
    reportingSeconds: sum((stats) => stats.reportingSeconds),
    availabilityPercent: mean((stats) => stats.availabilityPercent),
    temperaturePeakCelsius: max((stats) => stats.temperaturePeakCelsius),
    alerts: parts.reduce((total, stats) => total + stats.alerts, 0),
    criticalAlerts: parts.reduce((total, stats) => total + stats.criticalAlerts, 0),
  };
}

export interface HostUsageRow {
  hostId: string;
  displayName: string;
  stats: UsageStats | null;
}

/**
 * Every visible host's figures for one period, or for all the periods on show when `index` is null;
 * hosts with nothing in it come last with null stats.
 */
export function hostUsage(summary: UsageSummary, index: number | null): HostUsageRow[] {
  const periods: UsagePeriod[] = index == null ? summary.periods : summary.periods.slice(index, index + 1);
  return summary.hosts.map((host) => {
    const parts = periods.flatMap((period) =>
      period.hosts.filter((usage) => usage.hostId === host.hostId).map((usage) => usage.stats),
    );
    return { ...host, stats: parts.length === 0 ? null : combineStats(parts) };
  });
}

export type UsageSortKey =
  | "name"
  | "diskRead"
  | "diskWrite"
  | "received"
  | "sent"
  | "cpu"
  | "memory"
  | "availability"
  | "temperature"
  | "alerts";

const SORT_VALUES: Record<Exclude<UsageSortKey, "name">, (stats: UsageStats) => number | null> = {
  diskRead: (stats) => stats.diskReadBytes,
  diskWrite: (stats) => stats.diskWriteBytes,
  received: (stats) => stats.networkReceivedBytes,
  sent: (stats) => stats.networkSentBytes,
  cpu: (stats) => stats.cpuAveragePercent,
  memory: (stats) => stats.memoryAveragePercent,
  availability: (stats) => stats.availabilityPercent,
  temperature: (stats) => stats.temperaturePeakCelsius,
  alerts: (stats) => stats.alerts,
};

/** Sorts host rows; hosts without figures stay at the bottom whichever way the column runs. */
export function sortHostUsage(rows: HostUsageRow[], key: UsageSortKey, descending: boolean): HostUsageRow[] {
  const direction = descending ? -1 : 1;
  return [...rows].sort((a, b) => {
    if (key === "name") {
      return direction * a.displayName.localeCompare(b.displayName, undefined, { sensitivity: "base" });
    }
    const left = a.stats ? SORT_VALUES[key](a.stats) : null;
    const right = b.stats ? SORT_VALUES[key](b.stats) : null;
    if (left == null || right == null) {
      if (left == null && right == null) return a.displayName.localeCompare(b.displayName);
      return left == null ? 1 : -1;
    }
    return direction * (left - right) || a.displayName.localeCompare(b.displayName);
  });
}
