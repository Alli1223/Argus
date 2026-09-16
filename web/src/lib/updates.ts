import type { HostAgentUpdate, HostSummary } from "../api/types";

/** How long an agent may take to pick up an update before the page says it seems stuck. */
export const STALLED_AFTER_MS = 15 * 60 * 1000;

export type AgentUpdateState =
  | { kind: "none" }
  | { kind: "available"; version: string }
  | { kind: "updating"; version: string; stalled: boolean }
  | { kind: "failed"; error: string; available: string | null };

/** Where a host's agent stands with updates. */
export function agentUpdateState(update: HostAgentUpdate | null, nowMs: number): AgentUpdateState {
  if (!update) return { kind: "none" };
  if (update.requested) {
    const since = update.requestedAt ? Date.parse(update.requestedAt) : nowMs;
    return { kind: "updating", version: update.requested, stalled: nowMs - since > STALLED_AFTER_MS };
  }
  if (update.error) return { kind: "failed", error: update.error, available: update.available };
  return update.available ? { kind: "available", version: update.available } : { kind: "none" };
}

/** The hosts whose agent could update and has not been asked to, with the newest version on offer. */
export function outdatedAgents(hosts: Pick<HostSummary, "agentUpdate">[]): {
  count: number;
  version: string | null;
} {
  const waiting = hosts.filter((host) => host.agentUpdate?.available && !host.agentUpdate.requested);
  return { count: waiting.length, version: waiting[0]?.agentUpdate?.available ?? null };
}

/** What an administrator runs on the server to install a release. */
export function serverUpgradeCommands(tag: string): string {
  return [
    "git fetch --tags",
    `git checkout ${tag}`,
    "docker compose -f deploy/docker-compose.yml up -d --build",
  ].join("\n");
}
