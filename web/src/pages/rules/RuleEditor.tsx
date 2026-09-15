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
import type { AlertMetric, AlertOperator, AlertRule, AlertRuleRequest, AlertSeverity } from "../../api/types";
import { METRIC_LABELS } from "../../lib/alertMetrics";
import { normalizeTags, tagsError } from "../../lib/tags";
import {
  defaultThreshold,
  inputToThreshold,
  isFilesystemMetric,
  isPercentMetric,
  isServiceMetric,
  isStateMetric,
  thresholdSuffix,
  thresholdToInput,
} from "./ruleText";

type Scope = "all" | "host" | "tag";

interface RuleValues {
  name: string;
  metric: AlertMetric;
  operator: AlertOperator;
  /** In the metric's input unit (percent, MB/s or per core); a string while the field is empty. */
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

function initialValues(rule: AlertRule | null): RuleValues {
  if (!rule) {
    return {
      name: "",
      metric: "CpuUsage",
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
    operator: rule.operator,
    threshold: thresholdToInput(rule.metric, rule.threshold),
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
  const filter = values.resourceFilter.trim();
  const narrowable = isFilesystemMetric(values.metric) || isServiceMetric(values.metric);
  return {
    name: values.name.trim(),
    metric: values.metric,
    operator: state ? "Above" : values.operator,
    threshold: state ? 0 : inputToThreshold(values.metric, Number(values.threshold)),
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
        if (isPercentMetric(values.metric) && (value < 0 || value > 100))
          return "Choose a percentage from 0 to 100.";
        return value < 0 ? "The threshold cannot be negative." : null;
      },
      durationMinutes: (value, values) => {
        if (typeof value !== "number") return "Enter a number of minutes.";
        if (values.metric === "HostOffline" && value < 1) {
          return "Allow at least a minute, so brief network hiccups do not count as outages.";
        }
        return value < 0 || value > 1440 ? "Choose from 0 minutes to a day." : null;
      },
      hostId: (value, values) => (values.scope === "host" && !value ? "Choose a host." : null),
      tag: (value, values) => {
        if (values.scope !== "tag") return null;
        return value.trim() === "" ? "Choose a tag." : tagsError([value.trim()]);
      },
      resourceFilter: (value) => (value.length > 256 ? "Mount points can be up to 256 characters." : null),
    },
  });

  const values = form.values;
  const offline = values.metric === "HostOffline";
  const state = isStateMetric(values.metric);
  const hostOptions = (hosts.data ?? []).map((host) => ({ value: host.id, label: host.displayName }));
  const tagOptions = [...new Set((hosts.data ?? []).flatMap((host) => host.tags))].sort();

  // A different kind of metric gets a threshold that makes sense for it.
  const changeMetric = (next: string | null) => {
    if (!next) return;
    const metric = next as AlertMetric;
    form.setValues({
      metric,
      threshold:
        thresholdSuffix(metric) === thresholdSuffix(values.metric)
          ? values.threshold
          : defaultThreshold(metric),
      durationMinutes:
        metric === "HostOffline" && Number(values.durationMinutes) < 1 ? 5 : values.durationMinutes,
      // A mount point makes no sense as a service name, and the other way round.
      resourceFilter: isServiceMetric(metric) === isServiceMetric(values.metric) ? values.resourceFilter : "",
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

        {!state && (
          <Group gap="sm" align="flex-end" wrap="wrap">
            <Stack gap={4}>
              <Text id={`${ids}-operator`} fz="sm" fw={500}>
                Fires when it is
              </Text>
              <SegmentedControl
                aria-labelledby={`${ids}-operator`}
                data={[
                  { label: "Above", value: "Above" },
                  { label: "Below", value: "Below" },
                ]}
                {...form.getInputProps("operator")}
              />
            </Stack>
            <NumberInput
              label="Threshold"
              suffix={thresholdSuffix(values.metric)}
              min={0}
              max={isPercentMetric(values.metric) ? 100 : undefined}
              decimalScale={2}
              w={170}
              {...form.getInputProps("threshold")}
            />
          </Group>
        )}

        <NumberInput
          label={offline ? "After going quiet for" : "For at least"}
          description={
            offline
              ? "How long a host must go without reporting."
              : isServiceMetric(values.metric)
                ? "How long a service must stay failed. 0 raises the alert at the next check."
                : "How long the condition must hold. 0 fires on the first reading."
          }
          suffix=" min"
          min={offline ? 1 : 0}
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
