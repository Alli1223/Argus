import { describe, expect, it } from "vitest";
import type { AlertRule } from "../../api/types";
import {
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
  firingAlerts: 2,
  createdAt: "2026-09-15T10:00:00Z",
  updatedAt: "2026-09-15T10:00:00Z",
};

describe("describeRule", () => {
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
    });
  });
});
