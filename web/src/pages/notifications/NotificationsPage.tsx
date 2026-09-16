import { Alert, Box, Button, Group, Skeleton, Switch, Table, Text, VisuallyHidden } from "@mantine/core";
import { modals } from "@mantine/modals";
import { notifications } from "@mantine/notifications";
import {
  IconAlertTriangle,
  IconBrandDiscord,
  IconBrandSlack,
  IconCircleCheck,
  IconClock,
  IconMail,
  IconPlus,
  IconWebhook,
} from "@tabler/icons-react";
import { useState } from "react";
import {
  useDeleteNotificationChannel,
  useNotificationChannels,
  useNotificationSupport,
  useSaveNotificationChannel,
  useTestNotificationChannel,
} from "../../api/notifications";
import type { NotificationChannel, NotificationChannelKind } from "../../api/types";
import { PageHeader } from "../../components/PageHeader";
import { ErrorScreen } from "../../components/Screens";
import { useNow } from "../../lib/useNow";
import { ChannelEditor } from "./ChannelEditor";
import {
  KIND_LABELS,
  channelToRequest,
  describeDelivery,
  describeFilter,
  describeTarget,
  type DeliveryTone,
} from "./channelText";

const KIND_ICONS: Record<NotificationChannelKind, typeof IconMail> = {
  Email: IconMail,
  Slack: IconBrandSlack,
  Discord: IconBrandDiscord,
  Webhook: IconWebhook,
};

const TONES: Record<DeliveryTone, { icon: typeof IconMail | null; color: string | undefined }> = {
  sent: { icon: IconCircleCheck, color: "var(--mantine-color-healthy-text)" },
  problem: { icon: IconAlertTriangle, color: "var(--mantine-color-crimson-text)" },
  waiting: { icon: IconClock, color: undefined },
  none: { icon: null, color: undefined },
};

export function NotificationsPage() {
  const channels = useNotificationChannels();
  const support = useNotificationSupport();
  const now = useNow();
  // The channel stays set while the dialog animates closed, so its title does not flip mid-exit.
  const [editor, setEditor] = useState<{ opened: boolean; channel: NotificationChannel | null }>({
    opened: false,
    channel: null,
  });
  const open = (channel: NotificationChannel | null) => setEditor({ opened: true, channel });

  if (channels.isError) return <ErrorScreen error={channels.error} onRetry={() => void channels.refetch()} />;

  const emailBlocked =
    support.data?.email === false && channels.data?.some((channel) => channel.kind === "Email");

  return (
    <>
      <PageHeader
        title="Notifications"
        description="Where your alerts are sent. Each channel passes on the alerts it asks for when they fire, and when they resolve if you like."
        actions={
          <Button leftSection={<IconPlus size={16} />} onClick={() => open(null)}>
            New channel
          </Button>
        }
      />

      {emailBlocked && (
        <Alert color="bronze" title="Email is not set up on this server" mb="md">
          Your email channels cannot send until an administrator gives Argus a mail server to use.
        </Alert>
      )}

      <Box className="argus-surface" style={{ borderRadius: "var(--mantine-radius-sm)", overflowX: "auto" }}>
        <Table miw={860}>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>On</Table.Th>
              <Table.Th>Channel</Table.Th>
              <Table.Th>Sends</Table.Th>
              <Table.Th>Latest</Table.Th>
              <Table.Th>
                <VisuallyHidden>Actions</VisuallyHidden>
              </Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {channels.isPending &&
              Array.from({ length: 3 }, (_, index) => (
                <Table.Tr key={index}>
                  <Table.Td colSpan={5}>
                    <Skeleton h={28} />
                  </Table.Td>
                </Table.Tr>
              ))}
            {channels.data?.map((channel) => (
              <ChannelRow key={channel.id} channel={channel} now={now} onEdit={() => open(channel)} />
            ))}
            {channels.data?.length === 0 && (
              <Table.Tr>
                <Table.Td colSpan={5}>
                  <Group gap="sm" py="md" justify="center">
                    <Text fz="sm" c="dimmed">
                      No channels, so alerts only show up in Argus.
                    </Text>
                    <Button variant="subtle" size="compact-sm" onClick={() => open(null)}>
                      New channel
                    </Button>
                  </Group>
                </Table.Td>
              </Table.Tr>
            )}
          </Table.Tbody>
        </Table>
      </Box>

      <ChannelEditor
        channel={editor.channel}
        opened={editor.opened}
        onClose={() => setEditor((current) => ({ ...current, opened: false }))}
      />
    </>
  );
}

function ChannelRow({
  channel,
  now,
  onEdit,
}: {
  channel: NotificationChannel;
  now: number;
  onEdit: () => void;
}) {
  const save = useSaveNotificationChannel();
  const remove = useDeleteNotificationChannel();
  const test = useTestNotificationChannel();
  const KindIcon = KIND_ICONS[channel.kind];
  const latest = describeDelivery(channel.lastDelivery, now);
  const tone = TONES[latest.tone];

  const toggle = (enabled: boolean) =>
    save.mutate(
      { id: channel.id, channel: { ...channelToRequest(channel), enabled } },
      {
        onError: (error) =>
          notifications.show({
            color: "crimson",
            title: "The channel was not changed",
            message: error.message,
          }),
      },
    );

  const sendTest = () =>
    test.mutate(channel.id, {
      onSuccess: () =>
        notifications.show({
          color: "healthy",
          title: `Test sent to ${channel.name}`,
          message: "Check that it arrived.",
        }),
      onError: (error) =>
        notifications.show({ color: "crimson", title: "The test did not arrive", message: error.message }),
    });

  const confirmDelete = () =>
    modals.openConfirmModal({
      title: `Delete ${channel.name}?`,
      children: <Text fz="sm">Notifications still waiting to go out on it are dropped.</Text>,
      labels: { confirm: "Delete channel", cancel: "Keep it" },
      confirmProps: { color: "crimson" },
      onConfirm: () =>
        remove.mutate(channel.id, {
          onSuccess: () => notifications.show({ color: "healthy", message: `${channel.name} deleted.` }),
          onError: (error) =>
            notifications.show({
              color: "crimson",
              title: "The channel was not deleted",
              message: error.message,
            }),
        }),
    });

  return (
    <Table.Tr>
      <Table.Td>
        <Switch
          checked={channel.enabled}
          disabled={save.isPending}
          onChange={(event) => toggle(event.currentTarget.checked)}
          aria-label={channel.name}
        />
      </Table.Td>
      <Table.Td>
        <Group gap="xs" wrap="nowrap">
          <KindIcon size={18} aria-label={KIND_LABELS[channel.kind]} style={{ flex: "none" }} />
          <Box miw={0}>
            <Text fz="sm" fw={600} c={channel.enabled ? undefined : "dimmed"}>
              {channel.name}
            </Text>
            <Text fz="xs" c="dimmed" truncate="end" maw={320} title={describeTarget(channel)}>
              {describeTarget(channel)}
            </Text>
          </Box>
        </Group>
      </Table.Td>
      <Table.Td>
        <Text fz="sm">{describeFilter(channel)}</Text>
      </Table.Td>
      <Table.Td>
        <Group gap={6} wrap="nowrap" align="flex-start">
          {tone.icon && (
            <tone.icon size={15} color={tone.color} aria-hidden style={{ flex: "none", marginTop: 3 }} />
          )}
          <Text
            fz="sm"
            c={latest.tone === "none" ? "dimmed" : undefined}
            lineClamp={2}
            maw={280}
            title={latest.text}
          >
            {latest.text}
          </Text>
        </Group>
      </Table.Td>
      <Table.Td>
        <Group gap={4} justify="flex-end" wrap="nowrap">
          <Button
            variant="subtle"
            size="compact-sm"
            loading={test.isPending}
            onClick={sendTest}
            aria-label={`Send a test to ${channel.name}`}
          >
            Send test
          </Button>
          <Button variant="subtle" size="compact-sm" onClick={onEdit} aria-label={`Edit ${channel.name}`}>
            Edit
          </Button>
          <Button
            variant="subtle"
            color="crimson"
            size="compact-sm"
            onClick={confirmDelete}
            aria-label={`Delete ${channel.name}`}
          >
            Delete
          </Button>
        </Group>
      </Table.Td>
    </Table.Tr>
  );
}
