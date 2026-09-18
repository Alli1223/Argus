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

  it("lets the Linux agent watch Docker when asked", () => {
    const commands = installCommands("https://argus.example.com", "argus_et_abc", { docker: true });
    expect(commands.linux).toMatch(/ --token argus_et_abc --docker$/);
    expect(commands.windows).not.toContain("docker");
  });

  it("allows container actions, which include watching Docker", () => {
    const commands = installCommands("https://argus.example.com", "argus_et_abc", {
      docker: true,
      containerActions: true,
    });
    expect(commands.linux).toMatch(/ --token argus_et_abc --container-actions$/);
  });

  it("runs the agent from its image on machines without systemd", () => {
    const plain = installCommands("https://argus.example.com", "argus_et_abc").docker;
    expect(plain).toContain("docker run -d --name argus-agent --restart always");
    // The machine itself, read-only, with its processes and network.
    expect(plain).toContain("--mount type=bind,source=/,target=/host,readonly,bind-propagation=rslave");
    expect(plain).toContain("--network host --pid host");
    expect(plain).toContain("-e ARGUS_SERVERURL=https://argus.example.com");
    expect(plain).toContain("-e ARGUS_ENROLLMENTTOKEN=argus_et_abc");
    // Docker's socket is only given when the containers are wanted.
    expect(plain).not.toContain("docker.sock");
    expect(plain).not.toContain("ARGUS_CONTAINERACTIONS");
    expect(plain.trimEnd()).toMatch(/ghcr\.io\/[\w-]+\/argus-agent:latest$/);
  });

  it("gives the container Docker's socket, and actions when asked", () => {
    const watching = installCommands("https://argus.example.com", "argus_et_abc", { docker: true }).docker;
    expect(watching).toContain("-v /var/run/docker.sock:/var/run/docker.sock");
    expect(watching).not.toContain("ARGUS_CONTAINERACTIONS");

    const acting = installCommands("https://argus.example.com", "argus_et_abc", {
      containerActions: true,
    }).docker;
    expect(acting).toContain("-v /var/run/docker.sock:/var/run/docker.sock");
    expect(acting).toContain("-e ARGUS_CONTAINERACTIONS=true");
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
