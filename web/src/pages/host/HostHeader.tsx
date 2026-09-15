import { Anchor, Badge, Button, Group, Stack, Text, Title } from "@mantine/core";
import { useDisclosure } from "@mantine/hooks";
import { modals } from "@mantine/modals";
import { notifications } from "@mantine/notifications";
import { IconArrowLeft, IconSettings } from "@tabler/icons-react";
import { Link, useNavigate } from "react-router";
import { useDeleteHost } from "../../api/hosts";
import type { HostDetail } from "../../api/types";
import { HostStatusBadge, PlatformIcon } from "../../components/HostBits";
import { HostSettings } from "./HostSettings";

/** The host's name, status, what it runs and its notes, with its settings. */
export function HostHeader({ host }: { host: HostDetail }) {
  const navigate = useNavigate();
  const remove = useDeleteHost(host.id);
  const [settingsOpen, settings] = useDisclosure(false);

  const system = [
    host.hostname !== host.displayName ? host.hostname : null,
    host.osName ?? host.platform,
    host.kernelVersion ? `kernel ${host.kernelVersion}` : null,
    host.architecture,
  ]
    .filter(Boolean)
    .join(", ");

  // The deletion lives here rather than in the settings dialog, which has closed by the time it finishes.
  const confirmDelete = () =>
    modals.openConfirmModal({
      title: `Delete ${host.displayName}?`,
      children: (
        <Text fz="sm">
          This removes the host with all of its history and alerts, and cannot be undone. If the agent is
          still installed, uninstall it on the machine too: while its enrollment token is valid, it registers
          again as a new host.
        </Text>
      ),
      labels: { confirm: "Delete host", cancel: "Keep it" },
      confirmProps: { color: "crimson" },
      onConfirm: () =>
        remove.mutate(undefined, {
          onSuccess: () => {
            notifications.show({ color: "healthy", message: `${host.displayName} deleted.` });
            void navigate("/hosts", { replace: true });
          },
          onError: (error) =>
            notifications.show({
              color: "crimson",
              title: "The host was not deleted",
              message: error.message,
            }),
        }),
    });

  return (
    <Group justify="space-between" align="flex-end" wrap="wrap" gap="md" mb="lg">
      <Stack gap={6} miw={0}>
        <Anchor component={Link} to="/hosts" fz="sm" w="fit-content">
          <Group gap={4} wrap="nowrap">
            <IconArrowLeft size={14} aria-hidden />
            All hosts
          </Group>
        </Anchor>
        <Group gap="sm" wrap="wrap" align="center">
          <PlatformIcon platform={host.platform} size={24} />
          <Title order={1} fz={26}>
            {host.displayName}
          </Title>
          <HostStatusBadge status={host.status} />
        </Group>
        <Group gap={6} wrap="wrap">
          <Text c="dimmed" fz="sm">
            {system}
          </Text>
          {host.tags.map((tag) => (
            <Badge key={tag} size="sm" variant="outline" color="gray">
              {tag}
            </Badge>
          ))}
        </Group>
        {host.notes && (
          <Text fz="sm" maw={640} style={{ whiteSpace: "pre-wrap" }}>
            {host.notes}
          </Text>
        )}
      </Stack>
      <Button variant="default" leftSection={<IconSettings size={16} />} onClick={settings.open}>
        Settings
      </Button>
      <HostSettings host={host} opened={settingsOpen} onClose={settings.close} onDelete={confirmDelete} />
    </Group>
  );
}
