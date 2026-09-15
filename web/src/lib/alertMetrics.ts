import type { AlertMetric, AlertSeverity } from "../api/types";
import { formatPercent, formatRate } from "./format";

/** The status colour of each severity. It always comes with an icon or the severity's name. */
export const SEVERITY_COLORS: Record<AlertSeverity, string> = {
  Critical: "crimson",
  Warning: "bronze",
  Info: "iris",
};

/** What each rule metric measures, in words. */
export const METRIC_LABELS: Record<AlertMetric, string> = {
  CpuUsage: "CPU usage",
  MemoryUsage: "Memory usage",
  SwapUsage: "Swap usage",
  LoadPerCore: "Load per core",
  DiskIoUtilization: "Disk busy time",
  NetworkReceive: "Network received",
  NetworkTransmit: "Network sent",
  DiskUsage: "Disk space used",
  InodeUsage: "Inodes used",
  HostOffline: "Host offline",
  ServiceFailed: "Service failed",
};

type MetricUnit = "percent" | "bytesPerSecond" | "perCore" | "none";

export const METRIC_UNITS: Record<AlertMetric, MetricUnit> = {
  CpuUsage: "percent",
  MemoryUsage: "percent",
  SwapUsage: "percent",
  LoadPerCore: "perCore",
  DiskIoUtilization: "percent",
  NetworkReceive: "bytesPerSecond",
  NetworkTransmit: "bytesPerSecond",
  DiskUsage: "percent",
  InodeUsage: "percent",
  HostOffline: "none",
  ServiceFailed: "none",
};

/** A reading or threshold of a metric: "93.2%", "12.0 MB/s", "1.50 per core". */
export function formatMetricValue(metric: AlertMetric, value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) return "–";
  switch (METRIC_UNITS[metric]) {
    case "percent":
      return formatPercent(value, 1);
    case "bytesPerSecond":
      return formatRate(value);
    case "perCore":
      return `${value.toFixed(2)} per core`;
    default:
      return "–";
  }
}
