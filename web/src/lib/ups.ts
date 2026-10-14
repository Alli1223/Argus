import type { MetricSeries, UpsSnapshot } from "../api/types";
import type { ChartSeries } from "../components/charts/chartData";
import { SERIES_COLORS, type ChartScheme } from "../components/charts/chartPalette";
import type { Severity } from "./severity";

export type UpsState = "lowBattery" | "onBattery" | "replaceBattery" | "online" | "unknown";

// What NUT's status flags mean, for the ones worth naming beside the main state.
const FLAG_WORDS: Record<string, string> = {
  CHRG: "charging",
  DISCHRG: "discharging",
  RB: "battery needs replacing",
  OVER: "overloaded",
  BYPASS: "on bypass",
  TRIM: "trimming high voltage",
  BOOST: "boosting low voltage",
  CAL: "calibrating",
  OFF: "output off",
  FSD: "shutting down",
};

const flags = (status: string) => status.toUpperCase().split(/\s+/).filter(Boolean);

/** The state that matters most: a nearly empty battery, then running on it, then a worn-out one. */
export function upsState(ups: Pick<UpsSnapshot, "status" | "onBattery" | "lowBattery">): UpsState {
  const words = flags(ups.status);
  if (ups.lowBattery) return "lowBattery";
  if (ups.onBattery) return "onBattery";
  if (words.includes("RB")) return "replaceBattery";
  return words.includes("OL") ? "online" : "unknown";
}

export const UPS_STATE_LABELS: Record<UpsState, string> = {
  lowBattery: "Low battery",
  onBattery: "On battery",
  replaceBattery: "Replace battery",
  online: "On line power",
  unknown: "Unknown",
};

/** The status colour of each state. It always comes with the state's name. */
export const UPS_STATE_COLORS: Record<UpsState, string> = {
  lowBattery: "crimson",
  onBattery: "bronze",
  replaceBattery: "bronze",
  online: "healthy",
  unknown: "gray",
};

/** The other flags, in words: "charging", "battery needs replacing, overloaded". */
export function describeUpsFlags(status: string): string {
  return flags(status)
    .map((flag) => FLAG_WORDS[flag])
    .filter((word): word is string => word !== undefined && word !== "battery needs replacing")
    .join(", ");
}

/** How worrying a battery's charge is: the default UPS rules warn below 75% and are critical below 40%. */
export function chargeSeverity(percent: number): Severity {
  if (percent < 40) return "critical";
  return percent < 75 ? "warning" : "normal";
}

export interface UpsChart {
  key: string;
  title: string;
  description: string;
  series: ChartSeries[];
}

/** One chart per UPS, of its battery charge and load: both are percentages, so they share an axis. */
export function upsCharts(history: MetricSeries, scheme: ChartScheme): UpsChart[] {
  const names = Object.keys(history.series)
    .filter((key) => key.startsWith("charge:"))
    .map((key) => key.slice("charge:".length))
    .sort();
  const colors = SERIES_COLORS[scheme];
  return names.map((name) => {
    const battery = history.series[`onBattery:${name}`] ?? [];
    const outages = battery.filter((value) => value != null && value > 0).length;
    return {
      key: name,
      title: names.length > 1 ? `UPS ${name}` : "UPS",
      description:
        outages > 0
          ? `Battery charge and load. On battery in ${outages} ${outages === 1 ? "point" : "points"} of this range.`
          : "Battery charge and load.",
      series: [
        {
          key: `charge:${name}`,
          label: "Battery charge",
          color: colors[0],
          values: history.series[`charge:${name}`] ?? [],
        },
        {
          key: `load:${name}`,
          label: "Load",
          color: colors[1],
          values: history.series[`load:${name}`] ?? [],
        },
      ],
    };
  });
}
