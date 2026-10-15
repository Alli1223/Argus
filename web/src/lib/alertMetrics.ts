import type { AlertMetric, AlertSeverity } from "../api/types";
import { formatDuration, formatPercent, formatRate } from "./format";

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
  ContainerDown: "Container down",
  ContainerRestarts: "Container restarts",
  UpsOnBattery: "UPS on battery",
  UpsBatteryCharge: "UPS battery charge",
  UpsBatteryRuntime: "UPS battery runtime",
  UpsLoad: "UPS load",
  UpsChargeOnBattery: "UPS charge on battery",
  UpsRuntimeOnBattery: "UPS runtime on battery",
};

type MetricUnit = "percent" | "bytesPerSecond" | "perCore" | "restarts" | "seconds" | "none";

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
  ContainerDown: "none",
  ContainerRestarts: "restarts",
  UpsOnBattery: "none",
  UpsBatteryCharge: "percent",
  UpsBatteryRuntime: "seconds",
  UpsLoad: "percent",
  UpsChargeOnBattery: "percent",
  UpsRuntimeOnBattery: "seconds",
};

/** A reading or threshold of a metric: "93.2%", "12.0 MB/s", "1.50 per core", "4 restarts", "5m". */
export function formatMetricValue(metric: AlertMetric, value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) return "–";
  switch (METRIC_UNITS[metric]) {
    case "percent":
      return formatPercent(value, 1);
    case "bytesPerSecond":
      return formatRate(value);
    case "perCore":
      return `${value.toFixed(2)} per core`;
    case "restarts":
      return `${Math.round(value)} ${Math.round(value) === 1 ? "restart" : "restarts"}`;
    case "seconds":
      return formatDuration(value);
    default:
      return "–";
  }
}
