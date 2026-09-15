import { describe, expect, it } from "vitest";
import { BYTE_INCREMENTS, formatTick, formatValue } from "./chartData";

describe("formatTick", () => {
  it("drops needless decimals", () => {
    expect(formatTick(50, "percent")).toBe("50%");
    expect(formatTick(0, "bytesPerSecond")).toBe("0 B/s");
    expect(formatTick(500 * 1024, "bytesPerSecond")).toBe("500 KB/s");
    expect(formatTick(1024 ** 2, "bytesPerSecond")).toBe("1 MB/s");
    expect(formatTick(1.5 * 1024 ** 2, "bytesPerSecond")).toBe("1.5 MB/s");
    expect(formatTick(0.5, "load")).toBe("0.5");
    expect(formatTick(2, "load")).toBe("2");
  });
});

describe("formatValue", () => {
  it("formats by unit and shows a dash for gaps", () => {
    expect(formatValue(42.46, "percent")).toBe("42.5%");
    expect(formatValue(2048, "bytesPerSecond")).toBe("2.0 KB/s");
    expect(formatValue(0.734, "load")).toBe("0.73");
    expect(formatValue(null, "load")).toBe("–");
  });
});

describe("BYTE_INCREMENTS", () => {
  it("rises in binary multiples", () => {
    expect(BYTE_INCREMENTS).toContain(1024 ** 2);
    expect([...BYTE_INCREMENTS].sort((a, b) => a - b)).toEqual(BYTE_INCREMENTS);
  });
});
