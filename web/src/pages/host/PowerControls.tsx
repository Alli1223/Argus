import {
  Alert,
  Badge,
  Button,
  Group,
  Menu,
  Modal,
  SimpleGrid,
  Stack,
  Table,
  Text,
  TextInput,
} from "@mantine/core";
import { useForm } from "@mantine/form";
import { useDisclosure } from "@mantine/hooks";
import { modals } from "@mantine/modals";
import { notifications } from "@mantine/notifications";
import { IconChevronDown, IconPower, IconRefresh, IconSettings, IconSunrise } from "@tabler/icons-react";
import type { ReactNode } from "react";
import { useHostPower, usePowerCommand, useUpdatePowerSettings, type PowerCommand } from "../../api/power";
import type { HostDetail, HostPower, PowerActionInfo } from "../../api/types";
import { Section } from "../../components/Section";
import { formatAgo } from "../../lib/format";
import { ACTION_LABELS, describeAsker, describeMacs, describeRelays } from "../../lib/power";

const DONE: Record<PowerCommand, string> = {
  shutdown: "is shutting down",
  restart: "is restarting",
  wake: "was sent a Wake-on-LAN packet",
};

const FAILED: Record<PowerCommand, string> = {
  shutdown: "was not shut down",
  restart: "was not restarted",
  wake: "could not be woken",
};

/** Shut down, restart and wake; shutting down and restarting ask first, and only where the agent allows them. */
export function PowerMenu({ host }: { host: HostDetail }) {
  const power = useHostPower(host.id);
  const command = usePowerCommand(host.id);
  const enabled = power.data?.powerActionsEnabled ?? false;

  const run = (kind: PowerCommand) =>
    command.mutate(kind, {
      onSuccess: (action) =>
        notifications.show({
          color: "healthy",
          message: `${host.displayName} ${DONE[kind]}.${action.detail ? ` ${action.detail}.` : ""}`,
        }),
      onError: (error) =>
        notifications.show({
          color: "crimson",
          title: `${host.displayName} ${FAILED[kind]}`,
          message: error.message,
        }),
    });

  const confirm = (kind: "shutdown" | "restart") =>
    modals.openConfirmModal({
      title: `${kind === "shutdown" ? "Shut down" : "Restart"} ${host.displayName}?`,
      children: (
        <Text fz="sm">
          {kind === "shutdown"
            ? "The machine powers off at once, and whatever runs on it stops. Argus can wake it again only if its network card has Wake-on-LAN switched on."
            : "The machine restarts at once, and whatever runs on it stops until it is back."}
        </Text>
      ),
      labels: { confirm: kind === "shutdown" ? "Shut down" : "Restart", cancel: "Cancel" },
      confirmProps: { color: "crimson" },
      onConfirm: () => run(kind),
    });

  return (
    <Menu position="bottom-end" withinPortal>
      <Menu.Target>
        <Button
          variant="default"
          leftSection={<IconPower size={16} />}
          rightSection={<IconChevronDown size={14} />}
          loading={command.isPending}
        >
          Power
        </Button>
      </Menu.Target>
      <Menu.Dropdown maw={300}>
        <Menu.Item leftSection={<IconSunrise size={16} />} onClick={() => run("wake")}>
          Wake with Wake-on-LAN
        </Menu.Item>
        <Menu.Item
          leftSection={<IconRefresh size={16} />}
          disabled={!enabled}
          onClick={() => confirm("restart")}
        >
          Restart…
        </Menu.Item>
        <Menu.Item
          color="crimson"
          leftSection={<IconPower size={16} />}
          disabled={!enabled}
          onClick={() => confirm("shutdown")}
        >
          Shut down…
        </Menu.Item>
        {power.data && !enabled && (
          <Text fz="xs" c="dimmed" px="sm" py={6}>
            Shutting down and restarting are off on this machine. Its agent's install command switches them on
            with --power-actions.
          </Text>
        )}
      </Menu.Dropdown>
    </Menu>
  );
}

/** How Argus would wake the host, and the latest times it shut it down, restarted it or woke it. */
export function PowerSection({ hostId, now }: { hostId: string; now: number }) {
  const power = useHostPower(hostId);
  const [settingsOpen, settings] = useDisclosure(false);
  if (!power.data) return null;
  const data = power.data;

  return (
    <Section
      title="Power"
      mt="xl"
      action={
        <Button
          variant="subtle"
          size="compact-sm"
          leftSection={<IconSettings size={14} />}
          onClick={settings.open}
        >
          Wake-on-LAN settings
        </Button>
      }
    >
      <Stack gap="md" p="md">
        <SimpleGrid cols={{ base: 1, sm: 3 }} spacing="md">
          <Fact label="Wakes by">{describeMacs(data)}</Fact>
          <Fact label="Sends to">
            {data.wakeAddress ??
              (data.networks.length > 0 ? `Broadcast on ${data.networks.join(", ")}` : "Broadcast")}
          </Fact>
          <Fact label="Sent by">{describeRelays(data.wakeRelays)}</Fact>
        </SimpleGrid>
        {data.history.length > 0 && <PowerHistory actions={data.history} now={now} />}
      </Stack>
      <Modal opened={settingsOpen} onClose={settings.close} title="Wake-on-LAN settings">
        <WakeSettingsForm hostId={hostId} power={data} onClose={settings.close} />
      </Modal>
    </Section>
  );
}

function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <Text fz="xs" c="dimmed">
        {label}
      </Text>
      <Text fz="sm">{children}</Text>
    </div>
  );
}

function PowerHistory({ actions, now }: { actions: PowerActionInfo[]; now: number }) {
  return (
    <Table miw={560}>
      <Table.Thead>
        <Table.Tr>
          <Table.Th>When</Table.Th>
          <Table.Th>Action</Table.Th>
          <Table.Th>Asked by</Table.Th>
          <Table.Th>Outcome</Table.Th>
        </Table.Tr>
      </Table.Thead>
      <Table.Tbody>
        {actions.map((action) => (
          <Table.Tr key={action.id}>
            <Table.Td title={new Date(action.at).toLocaleString()}>{formatAgo(action.at, now)}</Table.Td>
            <Table.Td>{ACTION_LABELS[action.kind]}</Table.Td>
            <Table.Td>
              <Text fz="sm">{describeAsker(action)}</Text>
              {!action.requestedBy && (
                <Text fz="xs" c="dimmed">
                  {action.reason}
                </Text>
              )}
            </Table.Td>
            <Table.Td>
              <Badge color={action.succeeded ? "healthy" : "crimson"} variant="light">
                {action.succeeded ? "Done" : "Failed"}
              </Badge>
              {action.detail && (
                <Text fz="xs" c="dimmed" mt={2}>
                  {action.detail}
                </Text>
              )}
            </Table.Td>
          </Table.Tr>
        ))}
      </Table.Tbody>
    </Table>
  );
}

interface WakeValues {
  wakeMacAddress: string;
  wakeAddress: string;
}

const MAC = /^([0-9a-f]{2}[:-]?){5}[0-9a-f]{2}$/i;
const IPV4 = /^(\d{1,3}\.){3}\d{1,3}(\/\d{1,2})?$/;

function WakeSettingsForm({
  hostId,
  power,
  onClose,
}: {
  hostId: string;
  power: HostPower;
  onClose: () => void;
}) {
  const update = useUpdatePowerSettings(hostId);
  const form = useForm<WakeValues>({
    initialValues: { wakeMacAddress: power.wakeMacAddress ?? "", wakeAddress: power.wakeAddress ?? "" },
    validate: {
      wakeMacAddress: (value) =>
        value.trim() === "" || MAC.test(value.trim())
          ? null
          : "Give a hardware address such as aa:bb:cc:dd:ee:ff.",
      wakeAddress: (value) =>
        value.trim() === "" || IPV4.test(value.trim()) ? null : "Give an IPv4 address such as 192.168.1.255.",
    },
  });

  const submit = form.onSubmit((values) =>
    update.mutate(
      {
        wakeMacAddress: values.wakeMacAddress.trim() || null,
        wakeAddress: values.wakeAddress.trim() || null,
      },
      {
        onSuccess: () => {
          notifications.show({ color: "healthy", message: "Wake-on-LAN settings saved." });
          onClose();
        },
        onError: (error) => form.setErrors(error.fieldErrors),
      },
    ),
  );

  return (
    <form onSubmit={submit} noValidate>
      <Stack gap="md">
        <Text fz="sm">
          Agents on the same network send the packet, since the Argus server usually cannot reach it. The
          machine's network card must have Wake-on-LAN switched on, in its BIOS and its operating system.
        </Text>
        <TextInput
          label="Hardware address"
          description={
            power.macAddresses.length > 0
              ? `Empty uses what the agent reported: ${power.macAddresses.join(", ")}.`
              : "The agent has not reported one."
          }
          placeholder="aa:bb:cc:dd:ee:ff"
          data-autofocus
          {...form.getInputProps("wakeMacAddress")}
        />
        <TextInput
          label="Send to"
          description="Empty broadcasts on the machine's own networks. A network's broadcast address, such as 192.168.1.255, also works."
          placeholder="192.168.1.255"
          {...form.getInputProps("wakeAddress")}
        />
        {update.error && Object.keys(update.error.fieldErrors).length === 0 && (
          <Alert color="crimson" title={update.error.title}>
            {update.error.message}
          </Alert>
        )}
        <Group justify="flex-end" gap="sm">
          <Button variant="default" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={update.isPending}>
            Save
          </Button>
        </Group>
      </Stack>
    </form>
  );
}
