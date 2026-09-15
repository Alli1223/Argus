/** How worrying a utilisation reading is. Shown as colour, and always alongside the number. */
export type Severity = "normal" | "warning" | "critical";

export const WARNING_PERCENT = 75;
export const CRITICAL_PERCENT = 90;

export function severityOf(percent: number | null | undefined): Severity {
  if (percent == null) return "normal";
  if (percent >= CRITICAL_PERCENT) return "critical";
  if (percent >= WARNING_PERCENT) return "warning";
  return "normal";
}

/** Theme colours per severity: the meter fill uses the colour, its track a lighter step of it. */
export const severityColor: Record<Severity, string> = {
  normal: "iris",
  warning: "bronze",
  critical: "crimson",
};

const rank: Record<Severity, number> = { critical: 0, warning: 1, normal: 2 };

export function worst(severities: Severity[]): Severity {
  return severities.reduce<Severity>((a, b) => (rank[b] < rank[a] ? b : a), "normal");
}
