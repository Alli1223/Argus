import { describe, expect, it } from "vitest";
import { formatMetricValue, METRIC_LABELS } from "./alertMetrics";

describe("formatMetricValue", () => {
  it("formats by what the metric measures", () => {
    expect(formatMetricValue("CpuUsage", 93.2)).toBe("93.2%");
    expect(formatMetricValue("NetworkReceive", 2048)).toBe("2.0 KB/s");
    expect(formatMetricValue("LoadPerCore", 1.5)).toBe("1.50 per core");
  });

  it("shows a dash when there is no number to show", () => {
    expect(formatMetricValue("HostOffline", 300)).toBe("–");
    expect(formatMetricValue("DiskUsage", null)).toBe("–");
  });
});

describe("METRIC_LABELS", () => {
  it("names metrics in plain words", () => {
    expect(METRIC_LABELS.DiskIoUtilization).toBe("Disk busy time");
  });
});
