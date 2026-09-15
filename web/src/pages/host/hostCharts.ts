import type { HostPlatform, MetricSeries } from "../../api/types";
import type { ChartSeries, ChartUnit } from "../../components/charts/chartData";
import { LOAD_COLORS, SERIES_COLORS, type ChartScheme } from "../../components/charts/chartPalette";

export interface HostChart {
  key: string;
  title: string;
  description?: string;
  unit: ChartUnit;
  max?: number;
  series: ChartSeries[];
}

type Values = (number | null)[];

function percentOf(part: Values, whole: Values): Values {
  return part.map((value, index) => {
    const total = whole[index];
    return value != null && total != null && total > 0 ? (100 * value) / total : null;
  });
}

const hasReadings = (values: Values) => values.some((value) => value != null);

/**
 * The charts on a host's page. Each keeps to one unit, so one axis; series take the categorical
 * slots in order, and load averages the ordered ramp.
 */
export function hostCharts(
  metrics: MetricSeries,
  scheme: ChartScheme,
  platform: HostPlatform,
  threads: number,
): HostChart[] {
  const [first, second] = SERIES_COLORS[scheme];
  const pick = (key: string): Values => metrics.series[key] ?? metrics.time.map(() => null);
  const swap = percentOf(pick("swapUsed"), pick("swapTotal"));

  const charts: HostChart[] = [
    {
      key: "cpu",
      title: "CPU",
      description: "Share of all cores in use",
      unit: "percent",
      max: 100,
      series: [
        { key: "cpu", label: "Average", color: first, values: pick("cpu") },
        { key: "cpuMax", label: "Peak", color: second, values: pick("cpuMax") },
      ],
    },
    {
      key: "memory",
      title: "Memory",
      description: "In use by applications, not counting cache",
      unit: "percent",
      max: 100,
      series: [
        {
          key: "memory",
          label: "Memory",
          color: first,
          values: percentOf(pick("memUsed"), pick("memTotal")),
        },
        ...(hasReadings(swap) ? [{ key: "swap", label: "Swap", color: second, values: swap }] : []),
      ],
    },
  ];

  if (platform !== "Windows" && hasReadings(pick("load1"))) {
    const load = LOAD_COLORS[scheme];
    charts.push({
      key: "load",
      title: "Load average",
      description: `Processes running or waiting to run${threads > 0 ? `, on ${threads} threads` : ""}`,
      unit: "load",
      series: [
        { key: "load1", label: "1 minute", color: load.load1, values: pick("load1") },
        { key: "load5", label: "5 minutes", color: load.load5, values: pick("load5") },
        { key: "load15", label: "15 minutes", color: load.load15, values: pick("load15") },
      ],
    });
  }

  charts.push(
    {
      key: "disk",
      title: "Disk activity",
      description: "Reads and writes across all disks",
      unit: "bytesPerSecond",
      series: [
        { key: "diskRead", label: "Read", color: first, values: pick("diskRead") },
        { key: "diskWrite", label: "Write", color: second, values: pick("diskWrite") },
      ],
    },
    {
      key: "network",
      title: "Network",
      description: "Traffic on every interface except loopback",
      unit: "bytesPerSecond",
      series: [
        { key: "netRx", label: "Received", color: first, values: pick("netRx") },
        { key: "netTx", label: "Sent", color: second, values: pick("netTx") },
      ],
    },
  );

  return charts;
}
