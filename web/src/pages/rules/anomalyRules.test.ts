import { describe, expect, it } from "vitest";
import type { AlertRule } from "../../api/types";
import { describeRule, supportsAnomaly } from "./ruleText";

const anomalyRule: Pick<
  AlertRule,
  "metric" | "condition" | "operator" | "threshold" | "durationSeconds" | "resourceFilter"
> = {
  metric: "CpuUsage",
  condition: "Anomaly",
  operator: "Above",
  threshold: 3,
  durationSeconds: 600,
  resourceFilter: null,
};

describe("anomaly rules", () => {
  it("read as a phrase", () => {
    expect(describeRule(anomalyRule)).toBe("CPU usage unusually high (3σ) for 10 minutes");
    expect(
      describeRule({ ...anomalyRule, metric: "NetworkReceive", operator: "Below", threshold: 2.5 }),
    ).toBe("Network received unusually low (2.5σ) for 10 minutes");
  });

  it("work on host-wide measurements only", () => {
    expect(supportsAnomaly("CpuUsage")).toBe(true);
    expect(supportsAnomaly("NetworkTransmit")).toBe(true);
    expect(supportsAnomaly("DiskUsage")).toBe(false);
    expect(supportsAnomaly("HostOffline")).toBe(false);
    expect(supportsAnomaly("ServiceFailed")).toBe(false);
  });
});
