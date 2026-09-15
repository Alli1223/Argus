import type { MetricSeries } from "../../api/types";
import type { ChartSeries } from "../../components/charts/chartData";
import { SERIES_COLORS, type ChartScheme } from "../../components/charts/chartPalette";
import { formatPercent } from "../../lib/format";

export interface InterfaceChart {
  name: string;
  series: ChartSeries[];
}

const peak = (values: (number | null)[]) =>
  values.reduce<number>((top, value) => (value != null && value > top ? value : top), 0);

/**
 * One traffic chart per interface from the `rx:{name}` and `tx:{name}` series, busiest first.
 * Received and sent keep the same slots on every chart.
 */
export function interfaceCharts(network: MetricSeries, scheme: ChartScheme): InterfaceChart[] {
  const [first, second] = SERIES_COLORS[scheme];
  const gaps = network.time.map(() => null);
  const names = new Set(
    Object.keys(network.series)
      .filter((key) => key.startsWith("rx:") || key.startsWith("tx:"))
      .map((key) => key.slice(3)),
  );

  return [...names]
    .map((name) => {
      const received = network.series[`rx:${name}`] ?? gaps;
      const sent = network.series[`tx:${name}`] ?? gaps;
      return {
        name,
        busiest: Math.max(peak(received), peak(sent)),
        series: [
          { key: "rx", label: "Received", color: first, values: received },
          { key: "tx", label: "Sent", color: second, values: sent },
        ],
      };
    })
    .sort((a, b) => b.busiest - a.busiest || a.name.localeCompare(b.name))
    .map(({ name, series }) => ({ name, series }));
}

/** A sparkline in words: "from 41.2% to 43.0%". */
export function describeTrend(values: (number | null)[]): string {
  const readings = values.filter((value): value is number => value != null);
  if (readings.length === 0) return "no readings in this range";
  const first = formatPercent(readings[0], 1);
  const last = formatPercent(readings[readings.length - 1], 1);
  return readings.length === 1 ? last : `from ${first} to ${last}`;
}
