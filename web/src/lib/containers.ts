import type { ContainerEventInfo, ContainerSummary } from "../api/types";

/** Where a container's page is. */
export const containerPath = (hostId: string, name: string) =>
  `/hosts/${hostId}/containers/${encodeURIComponent(name)}`;

/** How often a container may restart in an hour before it counts as stuck in a loop. */
export const RESTART_LOOP = 3;

/**
 * Exit codes of a container that was stopped rather than crashed: clean exits, and the signals
 * `docker stop` and Ctrl+C send (SIGTERM, then SIGKILL after the timeout; SIGINT).
 */
const STOP_CODES = new Set([0, 130, 137, 143]);

export type ContainerTone = "healthy" | "warning" | "critical" | "muted";

export interface ContainerStatus {
  /** One or two words: "Running", "Unhealthy", "Exited (1)". */
  label: string;
  tone: ContainerTone;
  /** Whether it needs someone to look at it. */
  problem: boolean;
  /** A sentence on why, when the label alone does not say. */
  reason?: string;
}

/**
 * What a container's state means for the person watching it. Containers that exited cleanly or were
 * stopped are not problems: that is how one-off jobs end and how people stop things. Failing health
 * checks, crashes and restart loops are.
 */
export function containerStatus(container: ContainerSummary): ContainerStatus {
  const loop =
    container.restartsLastHour >= RESTART_LOOP
      ? `Restarted ${container.restartsLastHour} times in the last hour.`
      : undefined;

  switch (container.state) {
    case "running":
      if (container.health === "unhealthy")
        return { label: "Unhealthy", tone: "critical", problem: true, reason: loop };
      if (loop) return { label: "Restart loop", tone: "critical", problem: true, reason: loop };
      if (container.health === "starting") return { label: "Starting", tone: "warning", problem: false };
      return {
        label: container.health === "healthy" ? "Healthy" : "Running",
        tone: "healthy",
        problem: false,
      };
    case "restarting":
      return { label: "Restarting", tone: "critical", problem: true, reason: loop };
    case "exited":
      if (container.oomKilled)
        return { label: "Out of memory", tone: "critical", problem: true, reason: loop };
      if (container.exitCode != null && !STOP_CODES.has(container.exitCode)) {
        return { label: `Exited (${container.exitCode})`, tone: "critical", problem: true, reason: loop };
      }
      return { label: "Stopped", tone: "muted", problem: false, reason: loop };
    case "dead":
      return { label: "Dead", tone: "critical", problem: true };
    case "paused":
      return { label: "Paused", tone: "muted", problem: false };
    case "created":
      return { label: "Created", tone: "muted", problem: false };
    default:
      return {
        label: container.state === "removing" ? "Removing" : "Unknown",
        tone: "muted",
        problem: false,
      };
  }
}

const RANK: Record<ContainerTone, number> = { critical: 0, warning: 1, healthy: 2, muted: 3 };

/** Problems first, then running containers, then stopped ones; by name within each. */
export function sortContainers<T extends ContainerSummary>(containers: T[]): T[] {
  return [...containers].sort(
    (a, b) => RANK[containerStatus(a).tone] - RANK[containerStatus(b).tone] || a.name.localeCompare(b.name),
  );
}

export interface ContainerCounts {
  total: number;
  running: number;
  stopped: number;
  problems: number;
}

/** Whether a container is meant to be up: running, or being restarted by Docker to get it there. */
export function isUp(container: Pick<ContainerSummary, "state">): boolean {
  return container.state === "running" || container.state === "restarting";
}

export function countContainers(containers: ContainerSummary[]): ContainerCounts {
  return {
    total: containers.length,
    running: containers.filter(isUp).length,
    stopped: containers.filter((container) => !isUp(container)).length,
    problems: containers.filter((container) => containerStatus(container).problem).length,
  };
}

/** "3 running, 1 stopped, 2 need attention". */
export function describeCounts(counts: ContainerCounts): string {
  const parts = [`${counts.running} running`];
  if (counts.stopped > 0) parts.push(`${counts.stopped} stopped`);
  if (counts.problems > 0)
    parts.push(`${counts.problems} ${counts.problems === 1 ? "needs" : "need"} attention`);
  return parts.join(", ");
}

/** The Compose project and service a container belongs to, when it does: "shop / web". */
export function composeName(
  container: Pick<ContainerSummary, "composeProject" | "composeService">,
): string | null {
  return container.composeProject && container.composeService
    ? `${container.composeProject} / ${container.composeService}`
    : (container.composeProject ?? null);
}

/** An event in words: "Restarted 4 times", "Stopped, exit code 1". */
export function describeEvent(event: ContainerEventInfo): string {
  const detail = event.detail ? `, ${event.detail}` : "";
  switch (event.kind) {
    case "appeared":
      return `Created${event.detail ? ` from ${event.detail}` : ""}`;
    case "removed":
      return "Removed";
    case "recreated":
      return `Recreated${event.detail ? ` from ${event.detail}` : ""}`;
    case "started":
      return "Started";
    case "stopped":
      return `Stopped${detail}`;
    case "died":
      return `Died${detail}`;
    case "restarting":
      return "Restarting";
    case "restarted":
      return event.count === 1 ? "Restarted by Docker" : `Restarted by Docker ${event.count} times`;
    case "paused":
      return "Paused";
    case "unhealthy":
      return "Health check failing";
    case "healthy":
      return "Health check passing again";
    case "start-requested":
      return `Start asked for${event.detail ? ` by ${event.detail}` : ""}`;
    case "stop-requested":
      return `Stop asked for${event.detail ? ` by ${event.detail}` : ""}`;
    case "restart-requested":
      return `Restart asked for${event.detail ? ` by ${event.detail}` : ""}`;
    default:
      return event.kind;
  }
}

/** Whether an event is bad news, for its icon. */
export function isBadEvent(event: ContainerEventInfo): boolean {
  if (["died", "restarting", "restarted", "unhealthy"].includes(event.kind)) return true;
  if (event.kind !== "stopped") return false;
  const code = /^exit code (\d+)$/.exec(event.detail ?? "");
  return !code || !STOP_CODES.has(Number(code[1]));
}
