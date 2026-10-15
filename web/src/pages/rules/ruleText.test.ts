import { describe, expect, it } from "vitest";
import type { AlertRule } from "../../api/types";
import {
  describeActions,
  describeRule,
  describeScope,
  inputToThreshold,
  ruleToRequest,
  thresholdSuffix,
  thresholdToInput,
} from "./ruleText";

const rule: AlertRule = {
  id: "r1",
  name: "High CPU usage",
  metric: "CpuUsage",
  condition: "Threshold",
  operator: "Above",
  threshold: 90,
  durationSeconds: 300,
  severity: "Warning",
  hostId: null,
  hostName: null,
  tag: null,
  resourceFilter: null,
  enabled: true,
  shutdownHostIds: [],
  wakeOnResolve: false,
  firingAlerts: 2,
  createdAt: "2026-09-15T10:00:00Z",
  updatedAt: "2026-09-15T10:00:00Z",
};

describe("describeRule", () => {
  it("describes UPS rules", () => {
    expect(describeRule({ ...rule, metric: "UpsOnBattery", durationSeconds: 0 })).toBe("Any UPS on battery");
    expect(
      describeRule({
        ...rule,
        metric: "UpsChargeOnBattery",
        operator: "Below",
        threshold: 40,
        durationSeconds: 0,
      }),
    ).toBe("Any UPS on battery with charge below 40%");
    expect(
      describeRule({
        ...rule,
        metric: "UpsRuntimeOnBattery",
        operator: "Below",
        threshold: 300,
        durationSeconds: 0,
        resourceFilter: "ups",
      }),
    ).toBe("ups on battery with runtime below 5m");
    expect(
      describeRule({
        ...rule,
        metric: "UpsBatteryCharge",
        operator: "Below",
        threshold: 40,
        durationSeconds: 60,
      }),
    ).toBe("UPS battery charge below 40% for 1 minute");
    expect(
      describeRule({
        ...rule,
        metric: "UpsBatteryRuntime",
        operator: "Below",
        threshold: 300,
        resourceFilter: "ups",
        durationSeconds: 0,
      }),
    ).toBe("UPS battery runtime below 5m on ups");
  });

  it("describes container rules", () => {
    expect(describeRule({ ...rule, metric: "ContainerDown", durationSeconds: 120 })).toBe(
      "Any container down for 2 minutes",
    );
    expect(
      describeRule({
        ...rule,
        metric: "ContainerRestarts",
        threshold: 3,
        durationSeconds: 600,
        resourceFilter: "worker",
      }),
    ).toBe("worker restarted more than 3 times in 10 minutes");
    expect(thresholdSuffix("ContainerRestarts")).toBe(" restarts");
  });

  it("reads as a phrase", () => {
    expect(describeRule(rule)).toBe("CPU usage above 90% for 5 minutes");
    expect(
      describeRule({
        ...rule,
        metric: "DiskUsage",
        threshold: 85.5,
        resourceFilter: "/var",
        durationSeconds: 0,
      }),
    ).toBe("Disk space used above 85.5% on /var");
    expect(
      describeRule({
        ...rule,
        metric: "NetworkReceive",
        operator: "Below",
        threshold: 50 * 1024 ** 2,
        durationSeconds: 3_600,
      }),
    ).toBe("Network received below 50 MB/s for 1 hour");
    expect(describeRule({ ...rule, metric: "HostOffline", durationSeconds: 120 })).toBe(
      "Not reporting for 2 minutes",
    );
  });
});

describe("describeScope", () => {
  it("names the hosts a rule watches", () => {
    expect(describeScope(rule)).toBe("All hosts");
    expect(describeScope({ ...rule, hostId: "h1", hostName: "web-01" })).toBe("web-01");
    expect(describeScope({ ...rule, tag: "prod" })).toBe("Hosts tagged prod");
  });
});

describe("thresholds", () => {
  it("are typed in minutes for UPS runtimes and kept in seconds", () => {
    expect(thresholdToInput("UpsBatteryRuntime", 300)).toBe(5);
    expect(inputToThreshold("UpsBatteryRuntime", 2.5)).toBe(150);
    expect(thresholdSuffix("UpsBatteryRuntime")).toBe(" min");
  });

  it("are typed in MB/s for byte rates and kept in bytes", () => {
    expect(thresholdSuffix("NetworkTransmit")).toBe(" MB/s");
    expect(thresholdToInput("NetworkTransmit", 25 * 1024 ** 2)).toBe(25);
    expect(inputToThreshold("NetworkTransmit", 25)).toBe(25 * 1024 ** 2);
    expect(inputToThreshold("CpuUsage", 80)).toBe(80);
  });
});

describe("ruleToRequest", () => {
  it("keeps only what the server accepts", () => {
    expect(ruleToRequest(rule)).toEqual({
      name: "High CPU usage",
      metric: "CpuUsage",
      condition: "Threshold",
      operator: "Above",
      threshold: 90,
      durationSeconds: 300,
      severity: "Warning",
      hostId: null,
      tag: null,
      resourceFilter: null,
      enabled: true,
      shutdownHostIds: [],
      wakeOnResolve: false,
    });
  });
});

describe("describeActions", () => {
  it("says what a rule shuts down and wakes", () => {
    expect(describeActions(rule)).toBe("");
    expect(describeActions({ shutdownHostIds: ["h1"], wakeOnResolve: false })).toBe("Shuts down 1 machine");
    expect(describeActions({ shutdownHostIds: ["h1", "h2"], wakeOnResolve: true })).toBe(
      "Shuts down 2 machines, and wakes them after",
    );
  });
});
