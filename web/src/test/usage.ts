import type { UsageStats } from "../api/types";

/** Usage figures with nothing in them but what a test sets. */
export function stats(overrides: Partial<UsageStats> = {}): UsageStats {
  return {
    diskReadBytes: null,
    diskWriteBytes: null,
    networkReceivedBytes: null,
    networkSentBytes: null,
    cpuAveragePercent: null,
    cpuPeakPercent: null,
    memoryAveragePercent: null,
    memoryPeakPercent: null,
    reportingSeconds: null,
    availabilityPercent: null,
    temperaturePeakCelsius: null,
    alerts: 0,
    criticalAlerts: 0,
    ...overrides,
  };
}
