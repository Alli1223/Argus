import { describe, expect, it } from "vitest";
import type { AlertRule } from "../../api/types";
import { describeRule, isStateMetric } from "./ruleText";

const serviceRule: Pick<
  AlertRule,
  "metric" | "condition" | "operator" | "threshold" | "durationSeconds" | "resourceFilter"
> = {
  metric: "ServiceFailed",
  condition: "Threshold",
  operator: "Above",
  threshold: 0,
  durationSeconds: 0,
  resourceFilter: null,
};

describe("service rules", () => {
  it("read as a phrase", () => {
    expect(describeRule(serviceRule)).toBe("Any service failed");
    expect(describeRule({ ...serviceRule, resourceFilter: "nginx.service", durationSeconds: 300 })).toBe(
      "nginx.service failed for 5 minutes",
    );
  });

  it("have no threshold, like host offline", () => {
    expect(isStateMetric("ServiceFailed")).toBe(true);
    expect(isStateMetric("HostOffline")).toBe(true);
    expect(isStateMetric("CpuUsage")).toBe(false);
  });
});
