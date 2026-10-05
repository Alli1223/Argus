import { describe, expect, it } from "vitest";
import { rimFraction } from "./traffic";

describe("rimFraction", () => {
  it("is empty without traffic and full at a gigabit", () => {
    expect(rimFraction(null)).toBe(0);
    expect(rimFraction(0)).toBe(0);
    expect(rimFraction(125_000_000)).toBeCloseTo(1);
    expect(rimFraction(10_000_000_000)).toBe(1);
  });

  it("gives everyday rates room to differ", () => {
    const kilobyte = rimFraction(1_000);
    const hundredKilobytes = rimFraction(100_000);
    const tenMegabytes = rimFraction(10_000_000);

    expect(kilobyte).toBeGreaterThan(0);
    expect(kilobyte).toBeLessThan(0.1);
    expect(hundredKilobytes).toBeGreaterThan(0.3);
    expect(hundredKilobytes).toBeLessThan(0.5);
    expect(tenMegabytes).toBeGreaterThan(0.7);
    expect(tenMegabytes).toBeLessThan(0.9);
  });
});
