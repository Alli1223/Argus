import { describe, expect, it } from "vitest";
import type { MetricSeries } from "../api/types";
import { chargeSeverity, describeUpsFlags, upsCharts, upsState } from "./ups";

const ups = (status: string, onBattery = false, lowBattery = false) => ({ status, onBattery, lowBattery });

describe("upsState", () => {
  it("puts a nearly empty battery before running on it, and both before a worn-out one", () => {
    expect(upsState(ups("OB DISCHRG LB", true, true))).toBe("lowBattery");
    expect(upsState(ups("OB DISCHRG RB", true))).toBe("onBattery");
    expect(upsState(ups("OL RB"))).toBe("replaceBattery");
    expect(upsState(ups("OL CHRG"))).toBe("online");
    expect(upsState(ups(""))).toBe("unknown");
  });
});

describe("chargeSeverity", () => {
  it("worries as the battery empties, like the default rules", () => {
    expect(chargeSeverity(100)).toBe("normal");
    expect(chargeSeverity(75)).toBe("normal");
    expect(chargeSeverity(74)).toBe("warning");
    expect(chargeSeverity(16)).toBe("critical");
  });
});

describe("describeUpsFlags", () => {
  it("names the flags beside the main state", () => {
    expect(describeUpsFlags("OL CHRG")).toBe("charging");
    expect(describeUpsFlags("ob dischrg over")).toBe("discharging, overloaded");
    expect(describeUpsFlags("OL")).toBe("");
  });
});

describe("upsCharts", () => {
  const history = (series: Record<string, (number | null)[]>): MetricSeries => ({
    from: "2026-10-14T09:00:00Z",
    to: "2026-10-14T10:00:00Z",
    resolution: "raw",
    bucketSeconds: 60,
    time: [1, 2, 3],
    series,
  });

  it("draws charge and load for each UPS and counts the points it ran on battery", () => {
    const charts = upsCharts(
      history({
        "charge:ups": [100, 80, 60],
        "load:ups": [20, 21, 22],
        "runtime:ups": [900, 700, 500],
        "onBattery:ups": [0, 1, 1],
      }),
      "light",
    );

    expect(charts).toHaveLength(1);
    expect(charts[0].title).toBe("UPS");
    expect(charts[0].description).toBe("Battery charge and load. On battery in 2 points of this range.");
    expect(charts[0].series.map((series) => series.values)).toEqual([
      [100, 80, 60],
      [20, 21, 22],
    ]);
  });

  it("names each UPS when there are several", () => {
    const charts = upsCharts(history({ "charge:b": [1, 2, 3], "charge:a": [1, 2, 3] }), "dark");
    expect(charts.map((chart) => chart.title)).toEqual(["UPS a", "UPS b"]);
  });
});
