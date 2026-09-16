import type { HostAgentUpdate, HostSummary, ServerUpdateRun } from "../api/types";

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

/** What an administrator runs where Argus is installed to install a release by hand. */
export function serverUpgradeCommands(tag: string, version: string): string {
  return [
    "git fetch --tags",
    `git checkout ${tag}`,
    "grep -q '^ARGUS_VERSION=' deploy/.env || echo 'ARGUS_VERSION=' >> deploy/.env",
    `sed -i 's/^ARGUS_VERSION=.*/ARGUS_VERSION=${version}/' deploy/.env`,
    "docker compose -f deploy/docker-compose.yml up -d",
  ].join("\n");
}

export interface ServerUpdateStep {
  key: string;
  label: string;
  status: "done" | "current" | "waiting" | "failed";
}

// How far each state of an update has come through the four steps.
const REACHED: Partial<Record<ServerUpdateRun["state"], number>> = {
  starting: 0,
  downloading: 0,
  "backing-up": 1,
  deploying: 2,
  verifying: 3,
  succeeded: 4,
};

/** The steps of an update in progress, and which one it is on. Going back shows as a step of its own. */
export function serverUpdateSteps(run: Pick<ServerUpdateRun, "state" | "from" | "to">): ServerUpdateStep[] {
  const steps = [
    { key: "download", label: `Download Argus ${run.to}` },
    { key: "backup", label: "Back up the database" },
    { key: "start", label: `Start Argus ${run.to}` },
    { key: "check", label: "Check that it keeps running" },
  ];

  if (run.state === "rolling-back") {
    return [
      { ...steps[0], status: "done" },
      { ...steps[1], status: "done" },
      { ...steps[2], label: `Argus ${run.to} did not start properly`, status: "failed" },
      { key: "rollback", label: `Go back to Argus ${run.from}`, status: "current" },
    ];
  }

  const reached = REACHED[run.state] ?? 0;
  return steps.map((step, index) => ({
    ...step,
    status: index < reached ? "done" : index === reached ? "current" : "waiting",
  }));
}
