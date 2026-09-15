import { describe, expect, it } from "vitest";
import {
  DEFAULT_RANGE,
  describeBucket,
  rangeFromParams,
  rangeToParams,
  refreshInterval,
  resolveRange,
} from "./timeRange";

describe("rangeFromParams", () => {
  it("reads presets and zoomed windows", () => {
    expect(rangeFromParams(new URLSearchParams("range=24h"))).toEqual({ preset: "24h" });
    expect(rangeFromParams(new URLSearchParams("from=2026-09-15T10:00:00Z&to=2026-09-15T11:00:00Z"))).toEqual(
      {
        from: Date.parse("2026-09-15T10:00:00Z") / 1000,
        to: Date.parse("2026-09-15T11:00:00Z") / 1000,
      },
    );
  });

  it("falls back to the default for anything else", () => {
    expect(rangeFromParams(new URLSearchParams(""))).toEqual(DEFAULT_RANGE);
    expect(rangeFromParams(new URLSearchParams("range=2y"))).toEqual(DEFAULT_RANGE);
    expect(rangeFromParams(new URLSearchParams("from=soon&to=later"))).toEqual(DEFAULT_RANGE);
    expect(rangeFromParams(new URLSearchParams("from=2026-09-15T10:00:00Z&to=2026-09-15T10:00:30Z"))).toEqual(
      DEFAULT_RANGE,
    );
  });

  it("round-trips through the URL", () => {
    const zoomed = { from: 1_789_000_000, to: 1_789_003_600 };
    expect(rangeFromParams(new URLSearchParams(rangeToParams(zoomed)))).toEqual(zoomed);
    expect(rangeToParams({ preset: "7d" })).toEqual({ range: "7d" });
    expect(rangeToParams(DEFAULT_RANGE)).toEqual({});
  });
});

describe("resolveRange", () => {
  it("ends presets now", () => {
    expect(resolveRange({ preset: "1h" }, 1_789_000_000_500)).toEqual({
      from: 1_788_996_400,
      to: 1_789_000_000,
    });
  });

  it("keeps zoomed windows as they are", () => {
    expect(resolveRange({ from: 10, to: 100 }, 1_789_000_000_000)).toEqual({ from: 10, to: 100 });
  });
});

describe("refreshInterval", () => {
  it("refreshes live ranges and leaves zoomed windows alone", () => {
    expect(refreshInterval({ preset: "1h" })).toBe(30_000);
    expect(refreshInterval({ preset: "24h" })).toBe(60_000);
    expect(refreshInterval({ preset: "30d" })).toBe(300_000);
    expect(refreshInterval({ from: 1, to: 2 })).toBe(false);
  });
});

describe("describeBucket", () => {
  it("names the largest whole unit", () => {
    expect(describeBucket(15)).toBe("15 seconds");
    expect(describeBucket(60)).toBe("1 minute");
    expect(describeBucket(300)).toBe("5 minutes");
    expect(describeBucket(3_600)).toBe("1 hour");
    expect(describeBucket(86_400)).toBe("1 day");
    expect(describeBucket(90)).toBe("90 seconds");
  });
});
