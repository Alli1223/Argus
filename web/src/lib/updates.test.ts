import { describe, expect, it } from "vitest";
import type { HostAgentUpdate } from "../api/types";
import { agentUpdateState, outdatedAgents, serverUpgradeCommands } from "./updates";

const now = Date.parse("2026-09-16T12:00:00Z");

const update = (overrides: Partial<HostAgentUpdate>): HostAgentUpdate => ({
  available: null,
  requested: null,
  requestedAt: null,
  error: null,
  ...overrides,
});

describe("agent updates", () => {
  it("say where a host's agent stands", () => {
    expect(agentUpdateState(null, now)).toEqual({ kind: "none" });
    expect(agentUpdateState(update({ available: "0.3.0" }), now)).toEqual({
      kind: "available",
      version: "0.3.0",
    });
    expect(agentUpdateState(update({ available: "0.3.0", error: "Denied" }), now)).toEqual({
      kind: "failed",
      error: "Denied",
      available: "0.3.0",
    });
  });

  it("notice when an agent does not pick an update up", () => {
    const requested = update({ available: "0.3.0", requested: "0.3.0" });
    expect(agentUpdateState({ ...requested, requestedAt: "2026-09-16T11:55:00Z" }, now)).toEqual({
      kind: "updating",
      version: "0.3.0",
      stalled: false,
    });
    expect(agentUpdateState({ ...requested, requestedAt: "2026-09-16T11:30:00Z" }, now)).toMatchObject({
      stalled: true,
    });
  });

  it("count the agents that are out of date and not yet asked", () => {
    expect(
      outdatedAgents([
        { agentUpdate: update({ available: "0.3.0" }) },
        { agentUpdate: update({ available: "0.3.0", requested: "0.3.0" }) },
        { agentUpdate: null },
      ]),
    ).toEqual({ count: 1, version: "0.3.0" });
    expect(outdatedAgents([])).toEqual({ count: 0, version: null });
  });

  it("give the commands that install a server release", () => {
    expect(serverUpgradeCommands("v0.3.0")).toBe(
      "git fetch --tags\ngit checkout v0.3.0\ndocker compose -f deploy/docker-compose.yml up -d --build",
    );
  });
});
