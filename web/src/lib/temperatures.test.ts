import { describe, expect, it } from "vitest";
import type { MetricSeries } from "../api/types";
import { SERIES_COLORS } from "../components/charts/chartPalette";
import { describeDevice, parseSensorKey, temperatureCharts } from "./temperatures";

function history(keys: string[]): MetricSeries {
  return {
    from: "2026-09-16T10:00:00Z",
    to: "2026-09-16T10:02:00Z",
    resolution: "raw",
    bucketSeconds: 60,
    time: [1, 2],
    series: Object.fromEntries(keys.map((key, index) => [key, [40 + index, null]])),
  };
}

const labels = (chart: ReturnType<typeof temperatureCharts>[number]) =>
  chart.series.map((item) => item.label);

describe("parseSensorKey", () => {
  it("splits at the first slash, since sensor names may contain one", () => {
    expect(parseSensorKey("nvme0/Composite")).toEqual({
      key: "nvme0/Composite",
      device: "nvme0",
      name: "Composite",
    });
    expect(parseSensorKey("acpitz/TZ00/a")).toMatchObject({ device: "acpitz", name: "TZ00/a" });
    expect(parseSensorKey("odd")).toMatchObject({ device: "odd", name: "odd" });
  });
});

describe("describeDevice", () => {
  it("names common kinds of hardware", () => {
    expect(describeDevice("coretemp")).toBe("Processor");
    expect(describeDevice("coretemp.1")).toBe("Processor");
    expect(describeDevice("k10temp")).toBe("Processor");
    expect(describeDevice("nvme0")).toBe("NVMe drive");
    expect(describeDevice("sda")).toBe("Drive");
    expect(describeDevice("amdgpu 0000:03:00.0")).toBe("Graphics");
    expect(describeDevice("dell_smm")).toBeUndefined();
  });
});

describe("temperatureCharts", () => {
  it("gives each device a chart, processors first, with values taken by key", () => {
    const charts = temperatureCharts(
      history([
        "nvme0/Composite",
        "acpitz/temp1",
        "coretemp/Core 0",
        "coretemp/Package id 0",
        "dell_smm/temp1",
      ]),
      "light",
    );

    expect(charts.map((chart) => chart.title)).toEqual(["coretemp", "nvme0", "acpitz", "dell_smm"]);
    expect(charts[0].description).toBe("Processor");
    expect(charts[3].description).toBeUndefined();
    expect(charts[1].series[0].values).toEqual([40, null]);
  });

  it("puts one-off sensors before numbered families, in natural order", () => {
    const [chart] = temperatureCharts(
      history(["coretemp/Core 10", "coretemp/Core 2", "coretemp/Package id 0", "coretemp/Core 1"]),
      "light",
    );

    expect(labels(chart)).toEqual(["Package id 0", "Core 1", "Core 2", "Core 10"]);
  });

  it("splits devices with more sensors than colours into even charts", () => {
    const cores = Array.from({ length: 16 }, (_, core) => `coretemp/Core ${core}`);
    const charts = temperatureCharts(history(["coretemp/Package id 0", ...cores]), "light");

    expect(charts.map((chart) => chart.series.length)).toEqual([6, 6, 5]);
    expect(charts.map((chart) => chart.description)).toEqual([
      "Processor, sensors 1–6 of 17",
      "Processor, sensors 7–12 of 17",
      "Processor, sensors 13–17 of 17",
    ]);
    expect(new Set(charts.map((chart) => chart.key)).size).toBe(3);
    expect(labels(charts[0])[0]).toBe("Package id 0");
    expect(labels(charts[2]).at(-1)).toBe("Core 15");
  });

  it("describes split charts of unknown devices by their sensors alone", () => {
    const charts = temperatureCharts(
      history(Array.from({ length: 9 }, (_, index) => `dell_smm/temp${index + 1}`)),
      "light",
    );

    expect(charts.map((chart) => chart.description)).toEqual(["Sensors 1–5 of 9", "Sensors 6–9 of 9"]);
  });

  it("takes colours in slot order on every chart", () => {
    const charts = temperatureCharts(history(["nvme0/Composite", "nvme0/Sensor 1", "sda/temp1"]), "dark");

    expect(charts[0].series.map((item) => item.color)).toEqual(SERIES_COLORS.dark.slice(0, 2));
    expect(charts[1].series[0].color).toBe(SERIES_COLORS.dark[0]);
  });

  it("makes no charts for a host without sensors", () => {
    expect(temperatureCharts(history([]), "light")).toEqual([]);
  });
});
