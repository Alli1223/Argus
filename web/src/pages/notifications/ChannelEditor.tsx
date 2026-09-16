import { Alert, Button, Group, Modal, SegmentedControl, Stack, Switch, Text, TextInput } from "@mantine/core";
import { useForm } from "@mantine/form";
import { notifications } from "@mantine/notifications";
import { useId } from "react";
import { useNotificationSupport, useSaveNotificationChannel } from "../../api/notifications";
import type {
  AlertSeverity,
  NotificationChannel,
  NotificationChannelKind,
  NotificationChannelRequest,
} from "../../api/types";
import { KIND_LABELS, TARGET_FIELDS, reportTime, splitAddresses, targetError } from "./channelText";

const KIND_CHOICES = (Object.keys(KIND_LABELS) as NotificationChannelKind[]).map((kind) => ({
  value: kind,
  label: KIND_LABELS[kind],
}));

const SEVERITY_CHOICES: { label: string; value: AlertSeverity }[] = [
  { label: "Info", value: "Info" },
  { label: "Warning", value: "Warning" },
  { label: "Critical", value: "Critical" },
];

const NEW_CHANNEL: NotificationChannelRequest = {
  name: "",
  kind: "Email",
  target: "",
  minimumSeverity: "Warning",
  notifyOnResolved: true,
  enabled: true,
  dailyReport: false,
  weeklyReport: false,
};

interface ChannelEditorProps {
  /** The channel to change, or null for a new one. */
  channel: NotificationChannel | null;
  opened: boolean;
  onClose: () => void;
}

/** Create or change a notification channel. */
export function ChannelEditor({ channel, opened, onClose }: ChannelEditorProps) {
  return (
    <Modal opened={opened} onClose={onClose} title={channel ? "Edit channel" : "New channel"} size="lg">
      <ChannelForm channel={channel} onClose={onClose} />
    </Modal>
  );
}

// Mounted each time the dialog opens, so it starts from the channel as it is now.
function ChannelForm({ channel, onClose }: { channel: NotificationChannel | null; onClose: () => void }) {
  const save = useSaveNotificationChannel();
  const support = useNotificationSupport();
  const ids = useId();
  const form = useForm<NotificationChannelRequest>({
    initialValues: channel
      ? {
          name: channel.name,
          kind: channel.kind,
          target: channel.target,
          minimumSeverity: channel.minimumSeverity,
          notifyOnResolved: channel.notifyOnResolved,
          enabled: channel.enabled,
          dailyReport: channel.dailyReport,
          weeklyReport: channel.weeklyReport,
        }
      : NEW_CHANNEL,
    validate: {
      name: (value) => {
        if (value.trim() === "") return "Name the channel.";
        return value.trim().length > 100 ? "Names can be up to 100 characters." : null;
      },
      target: (value, values) => targetError(values.kind, value),
    },
  });

  const values = form.values;
  const field = TARGET_FIELDS[values.kind];

  // Addresses and webhook URLs do not carry over; one webhook URL can stand in for another.
  const changeKind = (next: string) => {
    const kind = next as NotificationChannelKind;
    const sameShape = (kind === "Email") === (values.kind === "Email");
    form.setValues({ kind, target: sameShape ? values.target : "" });
    form.clearFieldError("target");
  };

  const submit = form.onSubmit((submitted) =>
    save.mutate(
      {
        id: channel?.id,
        channel: {
          ...submitted,
          name: submitted.name.trim(),
          target:
            submitted.kind === "Email"
              ? splitAddresses(submitted.target).join(", ")
              : submitted.target.trim(),
        },
      },
      {
        onSuccess: () => {
          notifications.show({ color: "healthy", message: channel ? "Channel saved." : "Channel created." });
          onClose();
        },
        onError: (error) => form.setErrors(error.fieldErrors),
      },
    ),
  );

  return (
    <form onSubmit={submit} noValidate>
      <Stack gap="md">
        <TextInput
          label="Name"
          placeholder="Ops team"
          required
          data-autofocus
          {...form.getInputProps("name")}
        />

        <Stack gap={4}>
          <Text id={`${ids}-kind`} fz="sm" fw={500}>
            Send to
          </Text>
          <SegmentedControl
            aria-labelledby={`${ids}-kind`}
            w="fit-content"
            data={KIND_CHOICES}
            value={values.kind}
            onChange={changeKind}
          />
        </Stack>

        {values.kind === "Email" && support.data?.email === false && (
          <Alert color="bronze" title="Email is not set up on this server">
            Email channels cannot send until an administrator gives Argus a mail server to use.
          </Alert>
        )}

        <TextInput
          label={field.label}
          description={field.description}
          placeholder={field.placeholder}
          required
          {...form.getInputProps("target")}
        />

        <Stack gap={4}>
          <Text id={`${ids}-severity`} fz="sm" fw={500}>
            Send alerts of at least
          </Text>
          <SegmentedControl
            aria-labelledby={`${ids}-severity`}
            w="fit-content"
            data={SEVERITY_CHOICES}
            {...form.getInputProps("minimumSeverity")}
          />
        </Stack>

        <Switch
          label="Also send when alerts resolve"
          {...form.getInputProps("notifyOnResolved", { type: "checkbox" })}
        />
        <Switch
          label="Daily report"
          description={`A summary of the past day's alerts and hosts${support.data ? `, every day at ${reportTime(support.data, false)}` : ""}.`}
          {...form.getInputProps("dailyReport", { type: "checkbox" })}
        />
        <Switch
          label="Weekly report"
          description={`The same for the past week${support.data ? `, ${reportTime(support.data, true)}` : ""}.`}
          {...form.getInputProps("weeklyReport", { type: "checkbox" })}
        />
        <Switch
          label="Enabled"
          description="Switched off, a channel keeps its settings but sends nothing."
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
            {channel ? "Save changes" : "Create channel"}
          </Button>
        </Group>
      </Stack>
    </form>
  );
}
