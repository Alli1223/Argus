import {
  Alert,
  Autocomplete,
  Button,
  Group,
  Modal,
  NumberInput,
  SegmentedControl,
  Select,
  Stack,
  Switch,
  Text,
  TextInput,
} from "@mantine/core";
import { useForm } from "@mantine/form";
import { notifications } from "@mantine/notifications";
import { useId } from "react";
import { useSaveAlertRule } from "../../api/alerts";
import { useHosts } from "../../api/hosts";
import type {
  AlertCondition,
  AlertMetric,
  AlertOperator,
  AlertRule,
  AlertRuleRequest,
  AlertSeverity,
} from "../../api/types";
import { METRIC_LABELS } from "../../lib/alertMetrics";
import { normalizeTags, tagsError } from "../../lib/tags";
import {
  DEFAULT_SENSITIVITY,
  MIN_ANOMALY_MINUTES,
  defaultThreshold,
  inputToThreshold,
  isFilesystemMetric,
  isPercentMetric,
  isServiceMetric,
  isStateMetric,
  resourceKind,
  supportsAnomaly,
  thresholdSuffix,
  thresholdToInput,
} from "./ruleText";

type Scope = "all" | "host" | "tag";

interface RuleValues {
  name: string;
  metric: AlertMetric;
  condition: AlertCondition;
  operator: AlertOperator;
  /**
   * In the metric's input unit (percent, MB/s or per core), or standard deviations for anomaly
   * rules; a string while the field is empty.
   */
  threshold: number | string;
  durationMinutes: number | string;
  severity: AlertSeverity;
  scope: Scope;
  hostId: string | null;
  tag: string;
  resourceFilter: string;
  enabled: boolean;
}

const METRIC_OPTIONS = (Object.keys(METRIC_LABELS) as AlertMetric[]).map((metric) => ({
  value: metric,
  label: METRIC_LABELS[metric],
}));

const SCOPE_CHOICES: { label: string; value: Scope }[] = [
  { label: "All hosts", value: "all" },
  { label: "One host", value: "host" },
  { label: "Hosts with a tag", value: "tag" },
];

const CONDITION_CHOICES: { label: string; value: AlertCondition }[] = [
  { label: "A fixed threshold", value: "Threshold" },
  { label: "Its usual level", value: "Anomaly" },
];

/** Whether the rule, as it stands, compares with each host's usual level. */
const isAnomaly = (values: Pick<RuleValues, "metric" | "condition">) =>
  values.condition === "Anomaly" && supportsAnomaly(values.metric);

function initialValues(rule: AlertRule | null): RuleValues {
  if (!rule) {
    return {
      name: "",
      metric: "CpuUsage",
      condition: "Threshold",
      operator: "Above",
      threshold: 90,
      durationMinutes: 5,
      severity: "Warning",
      scope: "all",
      hostId: null,
      tag: "",
      resourceFilter: "",
      enabled: true,
    };
  }
  return {
    name: rule.name,
    metric: rule.metric,
    condition: rule.condition,
    operator: rule.operator,
    threshold: rule.condition === "Anomaly" ? rule.threshold : thresholdToInput(rule.metric, rule.threshold),
    durationMinutes: rule.durationSeconds / 60,
    severity: rule.severity,
    scope: rule.hostId ? "host" : rule.tag ? "tag" : "all",
    hostId: rule.hostId,
    tag: rule.tag ?? "",
    resourceFilter: rule.resourceFilter ?? "",
    enabled: rule.enabled,
  };
}

function toRequest(values: RuleValues): AlertRuleRequest {
  const state = isStateMetric(values.metric);
  const anomaly = isAnomaly(values);
  const threshold = Number(values.threshold);
  const filter = values.resourceFilter.trim();
  const narrowable = resourceKind(values.metric) !== null;
  return {
    name: values.name.trim(),
    metric: values.metric,
    condition: anomaly ? "Anomaly" : "Threshold",
    operator: state || values.metric === "ContainerRestarts" ? "Above" : values.operator,
    threshold: state ? 0 : anomaly ? threshold : inputToThreshold(values.metric, threshold),
    durationSeconds: Math.round(Number(values.durationMinutes) * 60),
    severity: values.severity,
    hostId: values.scope === "host" ? values.hostId : null,
    tag: values.scope === "tag" ? (normalizeTags([values.tag])[0] ?? null) : null,
    resourceFilter: narrowable && filter !== "" ? filter : null,
    enabled: values.enabled,
  };
}

interface RuleEditorProps {
  /** The rule to change, or null for a new one. */
  rule: AlertRule | null;
  opened: boolean;
  onClose: () => void;
}

/** Create or change an alert rule. */
export function RuleEditor({ rule, opened, onClose }: RuleEditorProps) {
  return (
    <Modal opened={opened} onClose={onClose} title={rule ? "Edit rule" : "New rule"} size="lg">
      <RuleForm rule={rule} onClose={onClose} />
    </Modal>
  );
}

// Mounted each time the dialog opens, so it starts from the rule as it is now.
function RuleForm({ rule, onClose }: { rule: AlertRule | null; onClose: () => void }) {
  const save = useSaveAlertRule();
  const hosts = useHosts();
  const ids = useId();
  const form = useForm<RuleValues>({
    initialValues: initialValues(rule),
    validate: {
      name: (value) => {
        if (value.trim() === "") return "Name the rule.";
        return value.trim().length > 100 ? "Names can be up to 100 characters." : null;
      },
      threshold: (value, values) => {
        if (isStateMetric(values.metric)) return null;
        if (typeof value !== "number") return "Enter a number.";
        if (isAnomaly(values)) {
          return value < 1 || value > 10 ? "Choose from 1 to 10 standard deviations." : null;
        }
        if (isPercentMetric(values.metric) && (value < 0 || value > 100))
          return "Choose a percentage from 0 to 100.";
        if (values.metric === "ContainerRestarts" && (!Number.isInteger(value) || value < 0 || value > 1000))
          return "Choose a whole number of restarts from 0 to 1000.";
        return value < 0 ? "The threshold cannot be negative." : null;
      },
      durationMinutes: (value, values) => {
        if (typeof value !== "number") return "Enter a number of minutes.";
        if (values.metric === "HostOffline" && value < 1) {
          return "Allow at least a minute, so brief network hiccups do not count as outages.";
        }
        if (values.metric === "ContainerRestarts" && value < 1) {
          return "Count over at least a minute: agents report restarts about once a minute.";
        }
        if (isAnomaly(values) && value < MIN_ANOMALY_MINUTES) {
          return "Allow at least 5 minutes, so one noisy reading cannot trip the rule.";
        }
        return value < 0 || value > 1440 ? "Choose from 0 minutes to a day." : null;
      },
      hostId: (value, values) => (values.scope === "host" && !value ? "Choose a host." : null),
      tag: (value, values) => {
        if (values.scope !== "tag") return null;
        return value.trim() === "" ? "Choose a tag." : tagsError([value.trim()]);
      },
      resourceFilter: (value) => (value.length > 256 ? "Names can be up to 256 characters." : null),
    },
  });

  const values = form.values;
  const offline = values.metric === "HostOffline";
  const restarts = values.metric === "ContainerRestarts";
  const state = isStateMetric(values.metric);
  const anomaly = isAnomaly(values);
  const hostOptions = (hosts.data ?? []).map((host) => ({ value: host.id, label: host.displayName }));
  const tagOptions = [...new Set((hosts.data ?? []).flatMap((host) => host.tags))].sort();

  // A sensitivity carries over to another metric an anomaly rule can watch; otherwise a different kind
  // of metric gets a threshold that makes sense for it.
  const changeMetric = (next: string | null) => {
    if (!next) return;
    const metric = next as AlertMetric;
    const keepsSensitivity = isAnomaly({ metric, condition: values.condition });
    const keepsThreshold =
      values.condition === "Threshold" && thresholdSuffix(metric) === thresholdSuffix(values.metric);
    form.setValues({
      metric,
      condition: keepsSensitivity ? "Anomaly" : "Threshold",
      threshold: keepsSensitivity || keepsThreshold ? values.threshold : defaultThreshold(metric),
      durationMinutes:
        metric === "HostOffline" && Number(values.durationMinutes) < 1
          ? 5
          : metric === "ContainerRestarts" && Number(values.durationMinutes) < 1
            ? 10
            : values.durationMinutes,
      // A mount point makes no sense as a service or container name, and so on.
      resourceFilter: resourceKind(metric) === resourceKind(values.metric) ? values.resourceFilter : "",
    });
  };

  const changeCondition = (next: string) => {
    const condition = next as AlertCondition;
    if (condition === values.condition) return;
    const toAnomaly = condition === "Anomaly";
    form.setValues({
      condition,
      threshold: toAnomaly ? DEFAULT_SENSITIVITY : defaultThreshold(values.metric),
      durationMinutes:
        toAnomaly && Number(values.durationMinutes) < MIN_ANOMALY_MINUTES ? 10 : values.durationMinutes,
    });
  };

  const submit = form.onSubmit((submitted) =>
    save.mutate(
      { id: rule?.id, rule: toRequest(submitted) },
      {
        onSuccess: () => {
          notifications.show({ color: "healthy", message: rule ? "Rule saved." : "Rule created." });
          onClose();
        },
        onError: (error) =>
          form.setErrors({ ...error.fieldErrors, durationMinutes: error.fieldErrors.durationSeconds }),
      },
    ),
  );

  return (
    <form onSubmit={submit} noValidate>
      <Stack gap="md">
        <TextInput
          label="Name"
          placeholder="Disk filling up"
          required
          data-autofocus
          {...form.getInputProps("name")}
        />
        <Select
          label="Watch"
          data={METRIC_OPTIONS}
          value={values.metric}
          onChange={changeMetric}
          allowDeselect={false}
        />

        {supportsAnomaly(values.metric) && (
          <Stack gap={4}>
            <Text id={`${ids}-condition`} fz="sm" fw={500}>
              Compare with
            </Text>
            <SegmentedControl
              aria-labelledby={`${ids}-condition`}
              w="fit-content"
              data={CONDITION_CHOICES}
              value={values.condition}
              onChange={changeCondition}
            />
            {anomaly && (
              <Text fz="xs" c="dimmed" maw={520}>
                Each host&apos;s usual level comes from its last week of readings, and the sensitivity counts
                standard deviations from it: higher means fewer alerts. The rule stays quiet until a host has
                a day of history.
              </Text>
            )}
          </Stack>
        )}

        {restarts && (
          <NumberInput
            label="Fires when Docker restarts a container more than"
            suffix=" times"
            min={0}
            max={1000}
            decimalScale={0}
            w={320}
            {...form.getInputProps("threshold")}
          />
        )}

        {!state && !restarts && (
          <Group gap="sm" align="flex-end" wrap="wrap">
            <Stack gap={4}>
              <Text id={`${ids}-operator`} fz="sm" fw={500}>
                Fires when it is
              </Text>
              <SegmentedControl
                aria-labelledby={`${ids}-operator`}
                data={
                  anomaly
                    ? [
                        { label: "Higher than usual", value: "Above" },
                        { label: "Lower than usual", value: "Below" },
                      ]
                    : [
                        { label: "Above", value: "Above" },
                        { label: "Below", value: "Below" },
                      ]
                }
                {...form.getInputProps("operator")}
              />
            </Stack>
            {anomaly ? (
              <NumberInput
                label="Sensitivity"
                suffix=" σ"
                min={1}
                max={10}
                step={0.5}
                decimalScale={1}
                w={140}
                {...form.getInputProps("threshold")}
              />
            ) : (
              <NumberInput
                label="Threshold"
                suffix={thresholdSuffix(values.metric)}
                min={0}
                max={isPercentMetric(values.metric) ? 100 : undefined}
                decimalScale={2}
                w={170}
                {...form.getInputProps("threshold")}
              />
            )}
          </Group>
        )}

        <NumberInput
          label={offline ? "After going quiet for" : restarts ? "Within" : "For at least"}
          description={
            offline
              ? "How long a host must go without reporting."
              : restarts
                ? "How far back to count restarts."
                : values.metric === "ContainerDown"
                  ? "How long a container must stay down. 0 raises the alert at the next report."
                  : isServiceMetric(values.metric)
                    ? "How long a service must stay failed. 0 raises the alert at the next check."
                    : anomaly
                      ? "How long the average must stay unusual. At least 5 minutes."
                      : "How long the condition must hold. 0 fires on the first reading."
          }
          suffix=" min"
          min={offline || restarts ? 1 : anomaly ? MIN_ANOMALY_MINUTES : 0}
          max={1440}
          decimalScale={1}
          w={220}
          {...form.getInputProps("durationMinutes")}
        />

        {isFilesystemMetric(values.metric) && (
          <TextInput
            label="Mount point"
            description="Leave empty to watch every filesystem."
            placeholder="/var"
            {...form.getInputProps("resourceFilter")}
          />
        )}
        {resourceKind(values.metric) === "container" && (
          <TextInput
            label="Container"
            description={
              values.metric === "ContainerDown"
                ? "Leave empty to watch every container; crashes, failing health checks and restart loops then count, but containers stopped on purpose do not. A rule naming a container fires whenever it is not up."
                : "Leave empty to watch every container."
            }
            placeholder="shop-web-1"
            {...form.getInputProps("resourceFilter")}
          />
        )}
        {isServiceMetric(values.metric) && (
          <TextInput
            label="Service"
            description="Leave empty to watch every service. Use the name systemd or Windows gives it."
            placeholder="nginx.service"
            {...form.getInputProps("resourceFilter")}
          />
        )}

        <Stack gap={4}>
          <Text id={`${ids}-severity`} fz="sm" fw={500}>
            Severity
          </Text>
          <SegmentedControl
            aria-labelledby={`${ids}-severity`}
            w="fit-content"
            data={["Info", "Warning", "Critical"]}
            {...form.getInputProps("severity")}
          />
        </Stack>

        <Stack gap={4}>
          <Text id={`${ids}-scope`} fz="sm" fw={500}>
            Applies to
          </Text>
          <SegmentedControl
            aria-labelledby={`${ids}-scope`}
            w="fit-content"
            data={SCOPE_CHOICES}
            {...form.getInputProps("scope")}
          />
        </Stack>
        {values.scope === "host" && (
          <Select
            label="Host"
            placeholder="Choose a host"
            data={hostOptions}
            searchable
            {...form.getInputProps("hostId")}
          />
        )}
        {values.scope === "tag" && (
          <Autocomplete label="Tag" placeholder="prod" data={tagOptions} {...form.getInputProps("tag")} />
        )}

        <Switch
          label="Enabled"
          description="Switched off, a rule keeps its settings but never fires."
          {...form.getInputProps("enabled", { type: "checkbox" })}
        />

        {save.error && Object.keys(save.error.fieldErrors).length === 0 && (
          <Alert color="crimson" title={save.error.title}>
            {save.error.message}
          </Alert>
        )}
        <Group justify="flex-end" gap="sm">
          <Button variant="default" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={save.isPending}>
            {rule ? "Save changes" : "Create rule"}
          </Button>
        </Group>
      </Stack>
    </form>
  );
}
