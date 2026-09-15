import { describe, expect, it } from "vitest";
import type { MetricSeries } from "../../api/types";
import { SERIES_COLORS } from "../../components/charts/chartPalette";
import { describeTrend, interfaceCharts } from "./hostResources";

function network(series: Record<string, (number | null)[]>): MetricSeries {
  return {
    from: "2026-09-15T10:00:00Z",
    to: "2026-09-15T10:02:00Z",
    resolution: "raw",
    bucketSeconds: 60,
    time: [1, 2],
    series,
  };
}

describe("interfaceCharts", () => {
  it("makes one chart per interface, busiest first", () => {
    const charts = interfaceCharts(
      network({ "rx:eth0": [10, 20], "tx:eth0": [5, 5], "rx:wg0": [900, 100], "tx:wg0": [1, 1] }),
      "light",
    );
    expect(charts.map((chart) => chart.name)).toEqual(["wg0", "eth0"]);
    expect(charts[0].series.map((item) => [item.label, item.color])).toEqual([
      ["Received", SERIES_COLORS.light[0]],
      ["Sent", SERIES_COLORS.light[1]],
    ]);
  });

  it("fills a missing direction with gaps", () => {
    const [chart] = interfaceCharts(network({ "rx:eth0": [1, 2] }), "dark");
    expect(chart.series[1].values).toEqual([null, null]);
  });
});

describe("describeTrend", () => {
  it("says where the line starts and ends", () => {
    expect(describeTrend([41.24, null, 43])).toBe("from 41.2% to 43.0%");
    expect(describeTrend([null, 12])).toBe("12.0%");
    expect(describeTrend([null])).toBe("no readings in this range");
  });
});
