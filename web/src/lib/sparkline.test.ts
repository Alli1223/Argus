import { describe, expect, it } from "vitest";
import { layoutSparkline } from "./sparkline";

describe("layoutSparkline", () => {
  it("draws nothing without readings", () => {
    expect(layoutSparkline([null, null], 100, 20)).toEqual({ path: "", end: null });
  });

  it("breaks the line at gaps", () => {
    const { path } = layoutSparkline([10, 20, null, 30], 100, 20, 0, 0);
    expect(path).toBe("M0 20L33.3 10M100 0");
  });

  it("ends at the newest reading", () => {
    expect(layoutSparkline([10, 30], 100, 20, 0, 0).end).toEqual({ x: 100, y: 0 });
  });

  it("keeps a nearly flat series in the middle", () => {
    expect(layoutSparkline([50, 50, 50], 90, 30, 2, 0).end).toEqual({ x: 90, y: 15 });
  });
});
