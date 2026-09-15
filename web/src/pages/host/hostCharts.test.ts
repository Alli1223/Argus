import { describe, expect, it } from "vitest";
import type { MetricSeries } from "../../api/types";
import { SERIES_COLORS } from "../../components/charts/chartPalette";
import { hostCharts } from "./hostCharts";

function metrics(series: Record<string, (number | null)[]>): MetricSeries {
  return {
    from: "2026-09-15T10:00:00Z",
    to: "2026-09-15T10:02:00Z",
    resolution: "raw",
    bucketSeconds: 60,
    time: [1, 2],
    series,
  };
}

const keys = (charts: ReturnType<typeof hostCharts>) => charts.map((chart) => chart.key);

describe("hostCharts", () => {
  it("turns memory bytes into a share of the total", () => {
    const charts = hostCharts(metrics({ memUsed: [4, null], memTotal: [16, 16] }), "light", "Linux", 8);
    const memory = charts.find((chart) => chart.key === "memory")!;
    expect(memory.series.map((item) => item.key)).toEqual(["memory"]);
    expect(memory.series[0].values).toEqual([25, null]);
  });

  it("adds swap only when the host has some", () => {
    const withSwap = metrics({ memUsed: [4, 4], memTotal: [16, 16], swapUsed: [1, 1], swapTotal: [4, 4] });
    const memory = hostCharts(withSwap, "light", "Linux", 8).find((chart) => chart.key === "memory")!;
    expect(memory.series.map((item) => item.label)).toEqual(["Memory", "Swap"]);
    expect(memory.series[1].values).toEqual([25, 25]);
  });

  it("shows load averages for hosts that report them", () => {
    const load = metrics({ load1: [1, 2], load5: [1, 1], load15: [0.5, 0.5] });
    expect(keys(hostCharts(load, "light", "Linux", 8))).toEqual(["cpu", "memory", "load", "disk", "network"]);
    expect(keys(hostCharts(load, "light", "Windows", 8))).toEqual(["cpu", "memory", "disk", "network"]);
    expect(keys(hostCharts(metrics({}), "light", "Linux", 8))).not.toContain("load");
  });

  it("takes series colours from the scheme's palette in slot order", () => {
    const cpu = hostCharts(metrics({ cpu: [1, 2], cpuMax: [2, 3] }), "dark", "Linux", 8)[0];
    expect(cpu.series.map((item) => item.color)).toEqual(SERIES_COLORS.dark.slice(0, 2));
  });
});
