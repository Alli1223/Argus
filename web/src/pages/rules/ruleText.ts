import type { AlertMetric, AlertRule, AlertRuleRequest } from "../../api/types";
import { formatMetricValue, METRIC_LABELS, METRIC_UNITS } from "../../lib/alertMetrics";
import { describeBucket } from "../../lib/timeRange";

const MB = 1024 ** 2;

export const isPercentMetric = (metric: AlertMetric) => METRIC_UNITS[metric] === "percent";
export const isFilesystemMetric = (metric: AlertMetric) => metric === "DiskUsage" || metric === "InodeUsage";

/** A threshold without needless decimals: "90%", "50 MB/s", "1.50 per core". */
export function formatThreshold(metric: AlertMetric, value: number): string {
  return formatMetricValue(metric, value).replace(/\.0(?=[% ])/, "");
}

type RuleCondition = Pick<
  AlertRule,
  "metric" | "operator" | "threshold" | "durationSeconds" | "resourceFilter"
>;

/** A rule's condition as a phrase: "CPU usage above 90% for 5 minutes". */
export function describeRule(rule: RuleCondition): string {
  const lasting = rule.durationSeconds > 0 ? ` for ${describeBucket(rule.durationSeconds)}` : "";
  if (rule.metric === "HostOffline") return `Not reporting${lasting}`;
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
