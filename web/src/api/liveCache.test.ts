import { describe, expect, it } from "vitest";
import { isLiveRange, withMetrics, withStatus } from "./liveCache";
import type { HostSummary, LatestMetrics } from "./types";

const host: HostSummary = {
  id: "h1",
  displayName: "web-01",
  hostname: "web-01",
  platform: "Linux",
  osName: "Ubuntu 24.04.3 LTS",
  tags: [],
  status: "Offline",
  lastSeenAt: "2026-09-15T11:00:00Z",
  agentVersion: "0.1.0",
  ownerId: "u1",
  latest: null,
};

const latest: LatestMetrics = {
  time: "2026-09-15T12:00:00Z",
  cpuPercent: 12,
  memoryPercent: 40,
  memoryUsedBytes: 4,
  memoryTotalBytes: 10,
  swapPercent: null,
  load1: 0.5,
  diskUsedPercent: 60,
  netRxBytesPerSec: 1,
  netTxBytesPerSec: 2,
  uptimeSeconds: 100,
};

describe("withMetrics", () => {
  it("gives the host its readings and marks it online", () => {
    const updated = withMetrics(host, { hostId: "h1", latest }, "2026-09-15T12:00:01Z");
    expect(updated).toMatchObject({ latest, status: "Online", lastSeenAt: "2026-09-15T12:00:01Z" });
  });

  it("leaves other hosts alone", () => {
    expect(withMetrics(host, { hostId: "h2", latest }, "2026-09-15T12:00:01Z")).toBe(host);
  });
});

describe("withStatus", () => {
  it("changes the status and keeps the last report when none is given", () => {
    expect(withStatus(host, { hostId: "h1", status: "Online", lastSeenAt: null })).toMatchObject({
      status: "Online",
      lastSeenAt: "2026-09-15T11:00:00Z",
    });
  });
});

describe("isLiveRange", () => {
  it("tells presets from zoomed windows", () => {
    expect(isLiveRange({ preset: "6h" })).toBe(true);
    expect(isLiveRange({ from: 1, to: 2 })).toBe(false);
    expect(isLiveRange(undefined)).toBe(false);
  });
});
