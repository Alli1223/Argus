import type { AlertSeverity } from "../../api/types";

export type StatusChoice = "Firing" | "Resolved" | "all";

/** What the alerts page shows, kept in the URL so a view can be shared or bookmarked. */
export interface AlertsView {
  status: StatusChoice;
  severity: AlertSeverity | null;
  hostId: string | null;
  page: number;
}

const SEVERITIES: readonly string[] = ["Critical", "Warning", "Info"];

/** Reads `?status=…&severity=…&host=…&page=…`; anything missing or unknown means the default. */
export function viewFromParams(params: URLSearchParams): AlertsView {
  const status = params.get("status");
  const severity = params.get("severity");
  const page = Number(params.get("page"));
  return {
    status: status === "Resolved" || status === "all" ? status : "Firing",
    severity: severity && SEVERITIES.includes(severity) ? (severity as AlertSeverity) : null,
    hostId: params.get("host") || null,
    page: Number.isInteger(page) && page > 1 ? page : 1,
  };
}

/** The query string for a view, leaving out whatever is the default. */
export function viewToParams(view: AlertsView): Record<string, string> {
  const params: Record<string, string> = {};
  if (view.status !== "Firing") params.status = view.status;
  if (view.severity) params.severity = view.severity;
  if (view.hostId) params.host = view.hostId;
  if (view.page > 1) params.page = String(view.page);
  return params;
}
