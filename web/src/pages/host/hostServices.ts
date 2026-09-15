import type { ServiceStatus } from "../../api/types";
import { formatAgo } from "../../lib/format";

/** A host's services in one line: "All services running, checked 30s ago." */
export function describeServices(status: ServiceStatus | undefined, nowMs: number): string {
  if (!status?.checkedAt) return "No service checks yet.";
  const checked = `checked ${formatAgo(status.checkedAt, nowMs)}`;
  const count = status.failures.length;
  if (count === 0) return `All services running, ${checked}.`;
  return `${count} ${count === 1 ? "service" : "services"} failing, ${checked}.`;
}
