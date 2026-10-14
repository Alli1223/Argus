import type { AlertMetric, AlertRule, AlertRuleRequest } from "../../api/types";
import { formatMetricValue, METRIC_LABELS, METRIC_UNITS } from "../../lib/alertMetrics";
import { describeBucket } from "../../lib/timeRange";

const MB = 1024 ** 2;

export const isPercentMetric = (metric: AlertMetric) => METRIC_UNITS[metric] === "percent";
export const isFilesystemMetric = (metric: AlertMetric) => metric === "DiskUsage" || metric === "InodeUsage";
export const isServiceMetric = (metric: AlertMetric) => metric === "ServiceFailed";
export const isContainerMetric = (metric: AlertMetric) =>
  metric === "ContainerDown" || metric === "ContainerRestarts";
export const isUpsMetric = (metric: AlertMetric) => metric.startsWith("Ups");

/** What a rule can be narrowed to: a mount point, a service, a container or a UPS, or nothing. */
export function resourceKind(metric: AlertMetric): "filesystem" | "service" | "container" | "ups" | null {
  if (isFilesystemMetric(metric)) return "filesystem";
  if (isServiceMetric(metric)) return "service";
  if (isUpsMetric(metric)) return "ups";
  return isContainerMetric(metric) ? "container" : null;
}

/** States rather than measurements: no threshold or direction, only how long they last. */
export const isStateMetric = (metric: AlertMetric) =>
  metric === "HostOffline" ||
  metric === "ServiceFailed" ||
  metric === "ContainerDown" ||
  metric === "UpsOnBattery";

/** Host-wide measurements, which anomaly rules can compare with each host's usual level. */
export const supportsAnomaly = (metric: AlertMetric) =>
  !isStateMetric(metric) && resourceKind(metric) === null;

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
  if (rule.metric === "ContainerDown") return `${rule.resourceFilter ?? "Any container"} down${lasting}`;
  if (rule.metric === "UpsOnBattery") return `${rule.resourceFilter ?? "Any UPS"} on battery${lasting}`;
  if (rule.metric === "ContainerRestarts") {
    const times = rule.threshold === 1 ? "once" : `${rule.threshold} times`;
    return `${rule.resourceFilter ?? "Any container"} restarted more than ${times} in ${describeBucket(rule.durationSeconds)}`;
  }
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

// Thresholds are typed in friendly units: percent, MB/s, per core or minutes. Byte rates are stored in
// bytes and runtimes in seconds.

export function thresholdToInput(metric: AlertMetric, threshold: number): number {
  const unit = METRIC_UNITS[metric];
  if (unit === "seconds") return threshold / 60;
  return unit === "bytesPerSecond" ? threshold / MB : threshold;
}

export function inputToThreshold(metric: AlertMetric, input: number): number {
  const unit = METRIC_UNITS[metric];
  if (unit === "seconds") return Math.round(input * 60);
  return unit === "bytesPerSecond" ? input * MB : input;
}

export function thresholdSuffix(metric: AlertMetric): string {
  const unit = METRIC_UNITS[metric];
  if (unit === "percent") return "%";
  if (unit === "bytesPerSecond") return " MB/s";
  if (unit === "restarts") return " restarts";
  if (unit === "seconds") return " min";
  return unit === "perCore" ? " per core" : "";
}

/** A sensible starting threshold, in input units, for when a rule switches to another kind of metric. */
export function defaultThreshold(metric: AlertMetric): number {
  if (metric === "UpsBatteryCharge") return 40;
  const unit = METRIC_UNITS[metric];
  if (unit === "seconds") return 5;
  if (unit === "percent") return 90;
  if (unit === "bytesPerSecond") return 100;
  if (unit === "restarts") return 3;
  return unit === "perCore" ? 1.5 : 0;
}
