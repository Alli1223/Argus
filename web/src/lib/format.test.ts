import { describe, expect, it } from "vitest";
import { formatAgo, formatBytes, formatDuration, formatPercent, formatRate } from "./format";

describe("formatAgo", () => {
  const now = Date.parse("2026-09-15T12:00:00Z");

  it("uses the largest sensible unit", () => {
    expect(formatAgo("2026-09-15T11:59:48Z", now)).toBe("12s ago");
    expect(formatAgo("2026-09-15T11:55:00Z", now)).toBe("5m ago");
    expect(formatAgo("2026-09-15T09:00:00Z", now)).toBe("3h ago");
    expect(formatAgo("2026-09-12T12:00:00Z", now)).toBe("3d ago");
  });

  it("never reports the future", () => {
    expect(formatAgo("2026-09-15T12:00:05Z", now)).toBe("0s ago");
    expect(formatAgo(null, now)).toBe("–");
  });
});

describe("formatBytes", () => {
  it("uses binary multiples", () => {
    expect(formatBytes(0)).toBe("0 B");
    expect(formatBytes(1536)).toBe("1.5 KB");
    expect(formatBytes(5 * 1024 ** 3)).toBe("5.0 GB");
  });

  it("never shows four integer digits", () => {
    expect(formatBytes(999)).toBe("999 B");
    expect(formatBytes(1000)).toBe("1.0 KB");
    expect(formatBytes(1023.3 * 1024)).toBe("1.0 MB");
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
