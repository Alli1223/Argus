import { describe, expect, it } from "vitest";
import { userStatus } from "./userStatus";

describe("userStatus", () => {
  it("says whether someone can sign in", () => {
    expect(userStatus({ isDisabled: false, isLockedOut: false })).toBe("Active");
    expect(userStatus({ isDisabled: false, isLockedOut: true })).toBe("Locked out");
    expect(userStatus({ isDisabled: true, isLockedOut: true })).toBe("Disabled");
  });
});
