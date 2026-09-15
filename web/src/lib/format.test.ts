import { describe, expect, it } from "vitest";
import { formatBytes, formatDuration, formatPercent, formatRate } from "./format";

describe("formatBytes", () => {
  it("uses binary multiples", () => {
    expect(formatBytes(0)).toBe("0 B");
    expect(formatBytes(1536)).toBe("1.5 KB");
    expect(formatBytes(5 * 1024 ** 3)).toBe("5.0 GB");
  });

  it("shows a dash for missing values", () => {
    expect(formatBytes(null)).toBe("–");
    expect(formatBytes(undefined)).toBe("–");
    expect(formatBytes(Number.NaN)).toBe("–");
  });
});

describe("formatRate", () => {
  it("appends per second", () => {
    expect(formatRate(2048)).toBe("2.0 KB/s");
  });
});

describe("formatPercent", () => {
  it("rounds to the requested digits", () => {
    expect(formatPercent(42.456)).toBe("42%");
    expect(formatPercent(42.456, 1)).toBe("42.5%");
  });
});

describe("formatDuration", () => {
  it("keeps the two largest units", () => {
    expect(formatDuration(30)).toBe("30s");
    expect(formatDuration(42 * 60)).toBe("42m");
    expect(formatDuration(5 * 3600 + 12 * 60)).toBe("5h 12m");
    expect(formatDuration(3 * 86_400 + 4 * 3600 + 5)).toBe("3d 4h");
  });
});
