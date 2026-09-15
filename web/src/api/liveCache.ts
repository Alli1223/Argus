import type { AlertSeverity, HostDetail, HostStatus, HostSummary, LatestMetrics } from "./types";

// What the live hub pushes (see the server's LiveContracts).

/** A host reported new metrics. */
export interface LiveHostMetrics {
  hostId: string;
  latest: LatestMetrics;
}

/** A host went offline or came back. */
export interface LiveHostStatus {
  hostId: string;
  status: HostStatus;
  lastSeenAt: string | null;
}

/** An alert fired or resolved. */
export interface LiveAlert {
  kind: "Fired" | "Resolved";
  alertId: string;
  hostId: string;
  title: string;
  severity: AlertSeverity;
  value: number | null;
  at: string;
}

type Host = HostSummary | HostDetail;

/** The host with its newest readings. Hearing from a host also means it is online. */
export function withMetrics<T extends Host>(host: T, update: LiveHostMetrics, receivedAt: string): T {
  if (host.id !== update.hostId) return host;
  return { ...host, latest: update.latest, status: "Online", lastSeenAt: receivedAt };
}

/** The host with its new status; the last report time only ever moves forward. */
export function withStatus<T extends Host>(host: T, update: LiveHostStatus): T {
  if (host.id !== update.hostId) return host;
  return { ...host, status: update.status, lastSeenAt: update.lastSeenAt ?? host.lastSeenAt };
}

/** Whether a chart query's range follows new readings (a preset) rather than a fixed, zoomed window. */
export function isLiveRange(range: unknown): boolean {
  return typeof range === "object" && range !== null && "preset" in range;
}
