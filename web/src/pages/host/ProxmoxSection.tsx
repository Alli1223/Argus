import { ActionIcon, Alert, Badge, Box, Group, Menu, Table, Text } from "@mantine/core";
import { modals } from "@mantine/modals";
import { notifications } from "@mantine/notifications";
import {
  IconAlertTriangle,
  IconDots,
  IconPlayerPlay,
  IconPlayerStop,
  IconPower,
  IconRefresh,
} from "@tabler/icons-react";
import { useGuestAction, useHostProxmox, type GuestAction } from "../../api/proxmox";
import type { ProxmoxGuest } from "../../api/types";
import { Section } from "../../components/Section";
import { UsageMeter } from "../../components/UsageMeter";
import { formatAgo, formatBytes, formatDuration } from "../../lib/format";
import { currentUptime, describeGuests, GUEST_KINDS, guestActions, STATUS_LABELS } from "../../lib/proxmox";

const DOT: Record<ProxmoxGuest["status"], string> = {
  running: "var(--mantine-color-healthy-filled)",
  paused: "var(--mantine-color-bronze-filled)",
  stopped: "var(--mantine-color-gray-5)",
  unknown: "var(--mantine-color-gray-5)",
};

const ACTIONS: Record<GuestAction, { label: string; done: string; failed: string }> = {
  start: { label: "Start", done: "is starting", failed: "was not started" },
  shutdown: { label: "Shut down…", done: "is shutting down", failed: "was not shut down" },
  reboot: { label: "Reboot…", done: "is rebooting", failed: "was not rebooted" },
  stop: { label: "Stop…", done: "was stopped", failed: "was not stopped" },
};

const ICONS: Record<GuestAction, typeof IconPower> = {
  start: IconPlayerPlay,
  shutdown: IconPower,
  reboot: IconRefresh,
  stop: IconPlayerStop,
};

const CONFIRM: Record<Exclude<GuestAction, "start">, { title: string; text: string; button: string }> = {
  shutdown: {
    title: "Shut down",
    text: "Proxmox VE asks it to shut down cleanly, as its power button would. A virtual machine needs the QEMU guest agent or ACPI to notice.",
    button: "Shut down",
  },
  reboot: {
    title: "Reboot",
    text: "Proxmox VE asks it to restart cleanly. Whatever runs on it stops until it is back.",
    button: "Reboot",
  },
  stop: {
    title: "Stop",
    text: "Proxmox VE stops it at once, as pulling its plug would. Anything it had not saved is lost.",
    button: "Stop now",
  },
};

/** A host's Proxmox VE virtual machines and containers. Hosts whose agent does not read Proxmox VE skip this. */
export function ProxmoxSection({ hostId, now }: { hostId: string; now: number }) {
  const report = useHostProxmox(hostId);
  const action = useGuestAction(hostId);
  const data = report.data;
  if (!data?.checkedAt) return null;
  const checkedAt = data.checkedAt;

  const run = (guest: ProxmoxGuest, kind: GuestAction) =>
    action.mutate(
      { guestId: guest.id, action: kind },
      {
        onSuccess: () =>
          notifications.show({ color: "healthy", message: `${guest.name} ${ACTIONS[kind].done}.` }),
        onError: (error) =>
          notifications.show({
            color: "crimson",
            title: `${guest.name} ${ACTIONS[kind].failed}`,
            message: error.message,
          }),
      },
    );

  const choose = (guest: ProxmoxGuest, kind: GuestAction) => {
    if (kind === "start") return run(guest, kind);
    const confirm = CONFIRM[kind];
    modals.openConfirmModal({
      title: `${confirm.title} ${guest.name}?`,
      children: <Text fz="sm">{confirm.text}</Text>,
      labels: { confirm: confirm.button, cancel: "Cancel" },
      confirmProps: { color: "crimson" },
      onConfirm: () => run(guest, kind),
    });
  };

  return (
    <Section
      title="Virtual machines and containers"
      mt="xl"
      action={
        <Text fz="xs" c="dimmed">
          {describeGuests(data.guests)}
          {data.node && ` on node ${data.node}`}, checked {formatAgo(checkedAt, now)}
        </Text>
      }
    >
      {data.problem && (
        <Alert
          color="bronze"
          icon={<IconAlertTriangle size={18} />}
          m="md"
          title="The agent cannot read Proxmox VE"
        >
          {data.problem}
          {data.guests.length > 0 && " The guests below are as it last saw them."}
        </Alert>
      )}
      {!data.actionsEnabled && data.guests.length > 0 && (
        <Text fz="xs" c="dimmed" px="md" pt="sm">
          Starting and stopping guests is off on this machine. Its agent's install command switches it on with
          --proxmox-actions.
        </Text>
      )}
      {data.guests.length === 0 ? (
        <Text fz="sm" c="dimmed" p="md">
          No virtual machines or containers on this node.
        </Text>
      ) : (
        <Table miw={760}>
          <Table.Thead>
            <Table.Tr>
              <Table.Th ta="right">ID</Table.Th>
              <Table.Th>Name</Table.Th>
              <Table.Th>Status</Table.Th>
              <Table.Th>CPU</Table.Th>
              <Table.Th ta="right">Memory</Table.Th>
              <Table.Th ta="right">Uptime</Table.Th>
              {data.actionsEnabled && <Table.Th aria-label="Actions" />}
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {data.guests.map((guest) => {
              const actions = guestActions(guest);
              const busy = action.isPending && action.variables?.guestId === guest.id;
              return (
                <Table.Tr key={guest.id}>
                  <Table.Td ta="right">{guest.id}</Table.Td>
                  <Table.Td>
                    <Group gap={6} wrap="nowrap">
                      <Text fz="sm" fw={600} truncate="end" maw={260} title={guest.name}>
                        {guest.name}
                      </Text>
                      <Badge size="xs" variant="light" color="gray">
                        {guest.template ? "Template" : GUEST_KINDS[guest.type]}
                      </Badge>
                    </Group>
                    {guest.tags.length > 0 && (
                      <Text fz="xs" c="dimmed">
                        {guest.tags.join(", ")}
                      </Text>
                    )}
                  </Table.Td>
                  <Table.Td>
                    <Group gap={6} wrap="nowrap">
                      <Box
                        w={8}
                        h={8}
                        aria-hidden
                        style={{ flexShrink: 0, borderRadius: "50%", background: DOT[guest.status] }}
                      />
                      <Text fz="sm">{STATUS_LABELS[guest.status]}</Text>
                    </Group>
                    <Text fz="xs" c="dimmed">
                      {guest.lock
                        ? `Locked: ${guest.lock}`
                        : `For ${formatDuration((now - Date.parse(guest.statusSince)) / 1000)}`}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    {guest.cpuPercent != null ? (
                      <UsageMeter value={guest.cpuPercent} label={`${guest.name} CPU`} />
                    ) : (
                      <Text fz="sm" c="dimmed">
                        –
                      </Text>
                    )}
                  </Table.Td>
                  <Table.Td ta="right">
                    {guest.memoryBytes != null && guest.maxMemoryBytes
                      ? `${formatBytes(guest.memoryBytes)} of ${formatBytes(guest.maxMemoryBytes)}`
                      : guest.maxMemoryBytes
                        ? formatBytes(guest.maxMemoryBytes)
                        : "–"}
                  </Table.Td>
                  <Table.Td ta="right">
                    {guest.status === "running" ? formatDuration(currentUptime(guest, checkedAt, now)) : "–"}
                  </Table.Td>
                  {data.actionsEnabled && (
                    <Table.Td ta="right">
                      {actions.length > 0 && (
                        <Menu position="bottom-end" withinPortal>
                          <Menu.Target>
                            <ActionIcon
                              variant="subtle"
                              color="gray"
                              aria-label={`Actions for ${guest.name}`}
                              loading={busy}
                            >
                              <IconDots size={16} />
                            </ActionIcon>
                          </Menu.Target>
                          <Menu.Dropdown>
                            {actions.map((kind) => {
                              const Icon = ICONS[kind];
                              return (
                                <Menu.Item
                                  key={kind}
                                  color={kind === "stop" ? "crimson" : undefined}
                                  leftSection={<Icon size={16} />}
                                  disabled={!data.listening}
                                  onClick={() => choose(guest, kind)}
                                >
                                  {ACTIONS[kind].label}
                                </Menu.Item>
                              );
                            })}
                            {!data.listening && (
                              <Text fz="xs" c="dimmed" px="sm" py={6} maw={260}>
                                The machine's agent is not connected right now.
                              </Text>
                            )}
                          </Menu.Dropdown>
                        </Menu>
                      )}
                    </Table.Td>
                  )}
                </Table.Tr>
              );
            })}
          </Table.Tbody>
        </Table>
      )}
    </Section>
  );
}
