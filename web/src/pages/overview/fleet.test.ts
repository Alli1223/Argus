import { describe, expect, it } from "vitest";
import type { FleetMetrics, HostSummary } from "../../api/types";
import { MAX_HOST_LINES, cpuSeries, fleetNow, networkSeries, peak } from "./fleet";

const host = (
  cpu: number,
  memory: number,
  rx: number | null,
  tx: number | null,
  online = true,
): HostSummary =>
  ({
    id: `${cpu}`,
    displayName: `host-${cpu}`,
    status: online ? "Online" : "Offline",
    latest: { cpuPercent: cpu, memoryPercent: memory, netRxBytesPerSec: rx, netTxBytesPerSec: tx },
  }) as HostSummary;

const fleet = (hostCount: number): FleetMetrics => ({
  totals: {
    from: "2026-10-05T10:00:00Z",
    to: "2026-10-05T11:00:00Z",
    resolution: "raw",
    bucketSeconds: 60,
    time: [1, 2, 3],
    series: { cpu: [10, 20, null], netRx: [5, null, 7], netTx: [1, 2, 3] },
  },
  hosts: Array.from({ length: hostCount }, (_, index) => ({
    hostId: `h${index}`,
    displayName: `h${index}`,
    cpu: [index, index * 2, null],
  })),
});

describe("fleetNow", () => {
  it("averages usage and adds up traffic over the online systems only", () => {
    const now = fleetNow([host(20, 40, 1_000, 10), host(60, 80, 3_000, null), host(99, 99, 1e9, 1e9, false)]);

    expect(now).toEqual({ reporting: 2, cpu: 40, memory: 60, received: 4_000, sent: 10 });
  });

  it("has no readings when nothing is online", () => {
    expect(fleetNow([host(50, 50, 1, 1, false)])).toEqual({
      reporting: 0,
      cpu: null,
      memory: null,
      received: null,
      sent: null,
    });
  });
});

describe("peak", () => {
  it("skips gaps", () => {
    expect(peak([null, 3, 9, null, 2])).toBe(9);
    expect(peak([null])).toBeNull();
    expect(peak(undefined)).toBeNull();
  });
});

describe("cpuSeries", () => {
  it("draws the average and each system while they fit", () => {
    const series = cpuSeries(fleet(2), "light");

    expect(series.map((line) => line.label)).toEqual(["Average", "h0", "h1"]);
    expect(series[0].values).toEqual([10, 20, null]);
    expect(new Set(series.map((line) => line.color)).size).toBe(3);
  });

  it("stands the busiest system in for many", () => {
    const series = cpuSeries(fleet(MAX_HOST_LINES + 1), "light");

    expect(series.map((line) => line.label)).toEqual(["Average", "Busiest system"]);
    expect(series[1].values).toEqual([MAX_HOST_LINES, MAX_HOST_LINES * 2, null]);
  });
});

describe("networkSeries", () => {
  it("has total received and sent", () => {
    const series = networkSeries(fleet(1), "dark");

    expect(series.map((line) => [line.label, line.values])).toEqual([
      ["Received", [5, null, 7]],
      ["Sent", [1, 2, 3]],
    ]);
  });
});
