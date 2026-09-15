import { describe, expect, it } from "vitest";
import type { ServiceFailure } from "../../api/types";
import { describeServices } from "./hostServices";

const now = Date.parse("2026-09-15T12:00:00Z");
const checkedAt = "2026-09-15T11:59:30Z";

const failure = (service: string): ServiceFailure => ({
  service,
  description: null,
  state: "failed",
  since: "2026-09-15T11:00:00Z",
});

describe("describeServices", () => {
  it("says when there is nothing to go on", () => {
    expect(describeServices(undefined, now)).toBe("No service checks yet.");
    expect(describeServices({ checkedAt: null, failures: [] }, now)).toBe("No service checks yet.");
  });

  it("says how many services are failing and when the agent checked", () => {
    expect(describeServices({ checkedAt, failures: [] }, now)).toBe("All services running, checked 30s ago.");
    expect(describeServices({ checkedAt, failures: [failure("cron.service")] }, now)).toBe(
      "1 service failing, checked 30s ago.",
    );
    expect(describeServices({ checkedAt, failures: [failure("a"), failure("b")] }, now)).toBe(
      "2 services failing, checked 30s ago.",
    );
  });
});
