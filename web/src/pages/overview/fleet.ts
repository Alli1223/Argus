import type { FleetMetrics, HostSummary } from "../../api/types";
import type { ChartSeries } from "../../components/charts/chartData";
import { SERIES_COLORS, type ChartScheme } from "../../components/charts/chartPalette";
import { trafficColors } from "../../components/watch/traffic";

/** The most lines on the CPU chart: the average and seven systems, one per colour slot. */
export const MAX_HOST_LINES = SERIES_COLORS.light.length - 1;

export interface FleetNow {
  /** Online systems with a reading. */
  reporting: number;
  /** Averages per system, in percent. */
  cpu: number | null;
  memory: number | null;
  /** Totals across systems, in bytes per second. */
  received: number | null;
  sent: number | null;
}

const average = (values: number[]) =>
  values.length === 0 ? null : values.reduce((sum, value) => sum + value, 0) / values.length;

const total = (values: (number | null | undefined)[]) => {
  const present = values.filter((value): value is number => value != null);
  return present.length === 0 ? null : present.reduce((sum, value) => sum + value, 0);
};

/** Right now, across the online systems: offline ones' last readings are history, not the present. */
export function fleetNow(hosts: HostSummary[]): FleetNow {
  const latest = hosts.filter((host) => host.status === "Online" && host.latest).map((host) => host.latest!);
  return {
    reporting: latest.length,
    cpu: average(latest.map((reading) => reading.cpuPercent)),
    memory: average(latest.map((reading) => reading.memoryPercent)),
    received: total(latest.map((reading) => reading.netRxBytesPerSec)),
    sent: total(latest.map((reading) => reading.netTxBytesPerSec)),
  };
}

/** The highest value in a series, or null when it has none. */
export function peak(values: (number | null)[] | undefined): number | null {
  const present = (values ?? []).filter((value): value is number => value != null);
  return present.length === 0 ? null : Math.max(...present);
}

/**
 * The fleet's average CPU, with each system's own line beside it. With more systems than colours, the
 * busiest system at each moment stands in for them.
 */
export function cpuSeries(fleet: FleetMetrics, scheme: ChartScheme): ChartSeries[] {
  const colors = SERIES_COLORS[scheme];
  const mean: ChartSeries = {
    key: "average",
    label: "Average",
    color: colors[0],
    values: fleet.totals.series.cpu ?? [],
  };

  if (fleet.hosts.length <= MAX_HOST_LINES) {
    return [
      mean,
      ...fleet.hosts.map((host, index) => ({
        key: host.hostId,
        label: host.displayName,
        color: colors[index + 1],
        values: host.cpu,
      })),
    ];
  }

  const busiest = fleet.totals.time.map((_, index) => peak(fleet.hosts.map((host) => host.cpu[index])));
  return [mean, { key: "busiest", label: "Busiest system", color: colors[1], values: busiest }];
}

/** Traffic in and out of every system together, in the same colours as the eyes' rims. */
export function networkSeries(fleet: FleetMetrics, scheme: ChartScheme): ChartSeries[] {
  const traffic = trafficColors(scheme);
  return [
    { key: "received", label: "Received", color: traffic.received, values: fleet.totals.series.netRx ?? [] },
    { key: "sent", label: "Sent", color: traffic.sent, values: fleet.totals.series.netTx ?? [] },
  ];
}
