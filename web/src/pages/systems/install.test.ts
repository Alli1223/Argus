import { describe, expect, it } from "vitest";
import type { EnrollmentTokenSummary } from "../../api/types";
import { describeExpiry, installCommands, isInsecureAddress, serverAddress, tokenStatus } from "./install";

const now = Date.parse("2026-09-15T12:00:00Z");

describe("serverAddress", () => {
  it("prefers the configured public address", () => {
    expect(serverAddress("https://argus.example.com/", "http://localhost:5173")).toBe(
      "https://argus.example.com",
    );
    expect(serverAddress(null, "http://localhost:5173")).toBe("http://localhost:5173");
  });
});

describe("installCommands", () => {
  it("puts the server and token into both commands", () => {
    const commands = installCommands("https://argus.example.com", "argus_et_abc");
    expect(commands.linux).toBe(
      "curl -fsSL https://argus.example.com/downloads/install.sh | sudo bash -s -- " +
        "--server https://argus.example.com --token argus_et_abc",
    );
    expect(commands.windows).toContain("Invoke-RestMethod https://argus.example.com/downloads/install.ps1");
    expect(commands.windows).toContain("-Server https://argus.example.com -Token argus_et_abc");
  });
});

describe("isInsecureAddress", () => {
  it("flags plain HTTP to other machines only", () => {
    expect(isInsecureAddress("http://argus.lan:5080")).toBe(true);
    expect(isInsecureAddress("http://localhost:5080")).toBe(false);
    expect(isInsecureAddress("https://argus.example.com")).toBe(false);
  });
});

describe("tokenStatus", () => {
  const token: EnrollmentTokenSummary = {
    id: "1",
    name: "Web servers",
    tokenPrefix: "argus_et_ab",
    tags: [],
    createdAt: "2026-09-15T10:00:00Z",
    expiresAt: null,
    maxUses: null,
    useCount: 0,
    lastUsedAt: null,
    revokedAt: null,
    isActive: true,
  };

  it("says why a token no longer works", () => {
    expect(tokenStatus(token, now)).toBe("Active");
    expect(tokenStatus({ ...token, revokedAt: "2026-09-15T11:00:00Z" }, now)).toBe("Revoked");
    expect(tokenStatus({ ...token, expiresAt: "2026-09-15T11:59:00Z" }, now)).toBe("Expired");
    expect(tokenStatus({ ...token, maxUses: 1, useCount: 1 }, now)).toBe("Used up");
  });
});

describe("describeExpiry", () => {
  it("says how long is left", () => {
    expect(describeExpiry(null, now)).toBe("Never");
    expect(describeExpiry("2026-09-15T11:00:00Z", now)).toBe("Expired");
    expect(describeExpiry("2026-09-15T12:45:00Z", now)).toBe("In 45m");
    expect(describeExpiry("2026-09-15T17:00:00Z", now)).toBe("In 5h");
    expect(describeExpiry("2026-09-18T12:00:00Z", now)).toBe("In 3d");
  });
});
