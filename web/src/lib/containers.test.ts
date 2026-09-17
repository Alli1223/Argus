import { describe, expect, it } from "vitest";
import type { ContainerSummary } from "../api/types";
import {
  containerPath,
  containerStatus,
  countContainers,
  describeCounts,
  describeEvent,
  isBadEvent,
  sortContainers,
} from "./containers";

export function container(overrides: Partial<ContainerSummary> = {}): ContainerSummary {
  return {
    name: "web",
    id: "c3cc787c3c4e",
    image: "nginx:1.29",
    state: "running",
    health: null,
    restartCount: 0,
    restartsLastHour: 0,
    exitCode: null,
    oomKilled: false,
    createdAt: "2026-09-17T10:00:00Z",
    startedAt: "2026-09-17T10:00:01Z",
    finishedAt: null,
    stateSince: "2026-09-17T10:00:01Z",
    restartPolicy: "unless-stopped",
    composeProject: null,
    composeService: null,
    ports: [],
    usage: null,
    ...overrides,
  };
}

const label = (overrides: Partial<ContainerSummary>) => {
  const status = containerStatus(container(overrides));
  return [status.label, status.problem];
};

describe("containerStatus", () => {
  it("counts failing health checks, crashes, deaths and restart loops as problems", () => {
    expect(label({ health: "unhealthy" })).toEqual(["Unhealthy", true]);
    expect(label({ state: "restarting" })).toEqual(["Restarting", true]);
    expect(label({ state: "exited", exitCode: 1 })).toEqual(["Exited (1)", true]);
    expect(label({ state: "exited", exitCode: 137, oomKilled: true })).toEqual(["Out of memory", true]);
    expect(label({ state: "dead" })).toEqual(["Dead", true]);
    expect(label({ restartsLastHour: 3 })).toEqual(["Restart loop", true]);
    expect(containerStatus(container({ restartsLastHour: 5 })).reason).toBe(
      "Restarted 5 times in the last hour.",
    );
  });

  it("does not count clean exits and stops as problems", () => {
    expect(label({})).toEqual(["Running", false]);
    expect(label({ health: "healthy" })).toEqual(["Healthy", false]);
    expect(label({ health: "starting" })).toEqual(["Starting", false]);
    expect(label({ state: "exited", exitCode: 0 })).toEqual(["Stopped", false]);
    // What docker stop leaves behind: SIGTERM, or SIGKILL after the timeout.
    expect(label({ state: "exited", exitCode: 143 })).toEqual(["Stopped", false]);
    expect(label({ state: "exited", exitCode: 137 })).toEqual(["Stopped", false]);
    expect(label({ state: "paused" })).toEqual(["Paused", false]);
    expect(label({ state: "created" })).toEqual(["Created", false]);
  });
});

describe("container lists", () => {
  const all = [
    container({ name: "zeta" }),
    container({ name: "job", state: "exited", exitCode: 0 }),
    container({ name: "api", health: "unhealthy" }),
    container({ name: "alpha" }),
  ];

  it("put problems first, then running, then stopped containers", () => {
    expect(sortContainers(all).map((item) => item.name)).toEqual(["api", "alpha", "zeta", "job"]);
  });

  it("are summed up in words", () => {
    expect(describeCounts(countContainers(all))).toBe("3 running, 1 stopped, 1 needs attention");
    expect(describeCounts(countContainers([container()]))).toBe("1 running");
  });

  it("link to each container's page", () => {
    expect(containerPath("h1", "shop web")).toBe("/hosts/h1/containers/shop%20web");
  });
});

describe("container events", () => {
  const event = (
    kind: Parameters<typeof describeEvent>[0]["kind"],
    detail: string | null = null,
    count = 1,
  ) => ({
    time: "2026-09-17T11:00:00Z",
    kind,
    detail,
    count,
  });

  it("read as sentences", () => {
    expect(describeEvent(event("restarted", null, 4))).toBe("Restarted by Docker 4 times");
    expect(describeEvent(event("stopped", "exit code 1"))).toBe("Stopped, exit code 1");
    expect(describeEvent(event("recreated", "nginx:1.29"))).toBe("Recreated from nginx:1.29");
    expect(describeEvent(event("unhealthy"))).toBe("Health check failing");
  });

  it("are bad news when something failed", () => {
    expect(isBadEvent(event("restarted"))).toBe(true);
    expect(isBadEvent(event("stopped", "exit code 2"))).toBe(true);
    expect(isBadEvent(event("stopped", "out of memory"))).toBe(true);
    expect(isBadEvent(event("stopped", "exit code 143"))).toBe(false);
    expect(isBadEvent(event("started"))).toBe(false);
  });
});
