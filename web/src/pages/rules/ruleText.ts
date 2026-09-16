import type { AlertMetric, AlertRule, AlertRuleRequest } from "../../api/types";
import { formatMetricValue, METRIC_LABELS, METRIC_UNITS } from "../../lib/alertMetrics";
import { describeBucket } from "../../lib/timeRange";

const MB = 1024 ** 2;

export const isPercentMetric = (metric: AlertMetric) => METRIC_UNITS[metric] === "percent";
export const isFilesystemMetric = (metric: AlertMetric) => metric === "DiskUsage" || metric === "InodeUsage";
export const isServiceMetric = (metric: AlertMetric) => metric === "ServiceFailed";

/** States rather than measurements: no threshold or direction, only how long they last. */
export const isStateMetric = (metric: AlertMetric) => metric === "HostOffline" || metric === "ServiceFailed";

/** Host-wide measurements, which anomaly rules can compare with each host's usual level. */
export const supportsAnomaly = (metric: AlertMetric) => !isStateMetric(metric) && !isFilesystemMetric(metric);

/** Where a new anomaly rule's sensitivity starts, in standard deviations. */
export const DEFAULT_SENSITIVITY = 3;

/** Anomaly rules average over at least this long, so one noisy reading cannot trip them. */
export const MIN_ANOMALY_MINUTES = 5;

/** A threshold without needless decimals: "90%", "50 MB/s", "1.50 per core". */
export function formatThreshold(metric: AlertMetric, value: number): string {
  return formatMetricValue(metric, value).replace(/\.0(?=[% ])/, "");
}

/** An anomaly rule's sensitivity in standard deviations: "3σ", "2.5σ". */
export function formatSensitivity(value: number): string {
  return `${Number(value.toFixed(1))}σ`;
}

type RuleCondition = Pick<
  AlertRule,
  "metric" | "condition" | "operator" | "threshold" | "durationSeconds" | "resourceFilter"
>;

/** A rule's condition as a phrase: "CPU usage above 90% for 5 minutes". */
export function describeRule(rule: RuleCondition): string {
  const lasting = rule.durationSeconds > 0 ? ` for ${describeBucket(rule.durationSeconds)}` : "";
  if (rule.metric === "HostOffline") return `Not reporting${lasting}`;
  if (rule.metric === "ServiceFailed") return `${rule.resourceFilter ?? "Any service"} failed${lasting}`;
  if (rule.condition === "Anomaly") {
    const unusually = rule.operator === "Above" ? "unusually high" : "unusually low";
    return `${METRIC_LABELS[rule.metric]} ${unusually} (${formatSensitivity(rule.threshold)})${lasting}`;
  }
  const direction = rule.operator === "Above" ? "above" : "below";
  const where = rule.resourceFilter ? ` on ${rule.resourceFilter}` : "";
  return `${METRIC_LABELS[rule.metric]} ${direction} ${formatThreshold(rule.metric, rule.threshold)}${where}${lasting}`;
}

/** Which hosts a rule watches: "All hosts", "web-01", "Hosts tagged prod". */
export function describeScope(rule: Pick<AlertRule, "hostId" | "hostName" | "tag">): string {
  if (rule.hostId) return rule.hostName ?? "One host";
  if (rule.tag) return `Hosts tagged ${rule.tag}`;
  return "All hosts";
}

/** The request that saves a rule as it stands, for example to switch it on or off. */
export function ruleToRequest(rule: AlertRule): AlertRuleRequest {
  const {
    name,
    metric,
    condition,
    operator,
    threshold,
    durationSeconds,
    severity,
    hostId,
    tag,
    resourceFilter,
    enabled,
  } = rule;
  return {
    name,
    metric,
    condition,
    operator,
    threshold,
    durationSeconds,
    severity,
    hostId,
    tag,
    resourceFilter,
    enabled,
  };
}

// Thresholds are typed in friendly units: percent, MB/s or per core. Byte rates are stored in bytes.

export function thresholdToInput(metric: AlertMetric, threshold: number): number {
  return METRIC_UNITS[metric] === "bytesPerSecond" ? threshold / MB : threshold;
}

export function inputToThreshold(metric: AlertMetric, input: number): number {
  return METRIC_UNITS[metric] === "bytesPerSecond" ? input * MB : input;
}

export function thresholdSuffix(metric: AlertMetric): string {
  const unit = METRIC_UNITS[metric];
  if (unit === "percent") return "%";
  if (unit === "bytesPerSecond") return " MB/s";
  return unit === "perCore" ? " per core" : "";
}

/** A sensible starting threshold, in input units, for when a rule switches to another kind of metric. */
export function defaultThreshold(metric: AlertMetric): number {
  const unit = METRIC_UNITS[metric];
  if (unit === "percent") return 90;
  if (unit === "bytesPerSecond") return 100;
  return unit === "perCore" ? 1.5 : 0;
}
