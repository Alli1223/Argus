import { Anchor, Box, Button, Group, Paper, SimpleGrid, Skeleton, Table, Text, Title } from "@mantine/core";
import { IconAlertTriangle, IconCircleCheck, IconPlus, IconUrgent } from "@tabler/icons-react";
import { Link } from "react-router";
import { useAlerts } from "../../api/alerts";
import { useDashboardSummary, useHosts } from "../../api/hosts";
import type { Alert, AlertSeverity } from "../../api/types";
import { PageHeader } from "../../components/PageHeader";
import { ErrorScreen, MessageScreen } from "../../components/Screens";
import { UsageMeter } from "../../components/UsageMeter";
import { Watch, WatchLegend } from "../../components/watch/Watch";
import { formatAgo } from "../../lib/format";
import { useNow } from "../../lib/useNow";

export function OverviewPage() {
  const hosts = useHosts();
  const summary = useDashboardSummary();
  const now = useNow();

  if (hosts.isError) return <ErrorScreen error={hosts.error} onRetry={() => void hosts.refetch()} />;

  const addSystem = (
    <Button component={Link} to="/systems" leftSection={<IconPlus size={16} />}>
      Add a system
    </Button>
  );

  if (hosts.isSuccess && hosts.data.length === 0) {
    return (
      <MessageScreen title="Nothing to watch yet" action={addSystem}>
        Install the Argus agent on a machine and it appears here within a minute, with alerts for high CPU,
        memory and disk use already switched on.
      </MessageScreen>
    );
  }

  const total = hosts.data?.length ?? 0;
  const online = hosts.data?.filter((host) => host.status === "Online").length ?? 0;

  return (
    <>
      <PageHeader
        title="Overview"
        description={hosts.isSuccess ? fleetSentence(online, total) : undefined}
        actions={addSystem}
      />

      <Paper className="argus-surface" radius="lg" p="lg" mb="lg">
        <Group justify="space-between" mb="md" align="baseline">
          <Title order={2} fz={17}>
            Systems, worst first
          </Title>
          <Anchor component={Link} to="/hosts" fz="sm">
            See them as a table
          </Anchor>
        </Group>
        {hosts.isPending ? <Skeleton height={140} /> : <Watch hosts={hosts.data ?? []} now={now} />}
        <WatchLegend />
      </Paper>

      <SimpleGrid cols={{ base: 1, md: 2 }} spacing="lg">
        <FiringAlerts now={now} />
        <Section title="Busiest right now">
          {summary.data && summary.data.busiestByCpu.length > 0 ? (
            <Table>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>Host</Table.Th>
                  <Table.Th>CPU</Table.Th>
                  <Table.Th>Memory</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {summary.data.busiestByCpu.map((host) => (
                  <Table.Tr key={host.id}>
                    <Table.Td>
                      <Anchor component={Link} to={`/hosts/${host.id}`} fz="sm" fw={600}>
                        {host.displayName}
                      </Anchor>
                    </Table.Td>
                    <Table.Td>
                      <UsageMeter value={host.latest?.cpuPercent} label={`${host.displayName} CPU`} />
                    </Table.Td>
                    <Table.Td>
                      <UsageMeter value={host.latest?.memoryPercent} label={`${host.displayName} memory`} />
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          ) : (
            <Text c="dimmed" fz="sm" p="md">
              {summary.isPending ? "Loading…" : "No readings from online systems yet."}
            </Text>
          )}
        </Section>
      </SimpleGrid>
    </>
  );
}

function fleetSentence(online: number, total: number) {
  if (total === online) return total === 1 ? "Your system is online." : `All ${total} systems are online.`;
  return `${online} of ${total} systems online.`;
}

function Section({
  title,
  action,
  children,
}: {
  title: string;
  action?: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <Box>
      <Group justify="space-between" mb="xs" align="baseline">
        <Title order={2} fz={17}>
          {title}
        </Title>
        {action}
      </Group>
      <Box className="argus-surface" style={{ borderRadius: "var(--mantine-radius-sm)", overflowX: "auto" }}>
        {children}
      </Box>
    </Box>
  );
}

const severityIcon: Record<AlertSeverity, typeof IconUrgent> = {
  Critical: IconUrgent,
  Warning: IconAlertTriangle,
  Info: IconCircleCheck,
};

const severityColorName: Record<AlertSeverity, string> = {
  Critical: "crimson",
  Warning: "bronze",
  Info: "iris",
};

/** Severity as an icon and a word in the severity's colour, never colour alone. */
export function SeverityLabel({ severity }: { severity: AlertSeverity }) {
  const Icon = severityIcon[severity];
  const color = `var(--mantine-color-${severityColorName[severity]}-text)`;
  return (
    <Group gap={4} wrap="nowrap" c={color}>
      <Icon size={15} aria-hidden />
      <Text fz="sm" fw={600} c={color}>
        {severity}
      </Text>
    </Group>
  );
}

function FiringAlerts({ now }: { now: number }) {
  const alerts = useAlerts({ status: "Firing", pageSize: 8 });
  const items: Alert[] = alerts.data?.items ?? [];

  return (
    <Section
      title="Firing alerts"
      action={
        <Anchor component={Link} to="/alerts" fz="sm">
          All alerts
        </Anchor>
      }
    >
      {items.length > 0 ? (
        <Table>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Severity</Table.Th>
              <Table.Th>What</Table.Th>
              <Table.Th>Since</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {items.map((alert) => (
              <Table.Tr key={alert.id}>
                <Table.Td>
                  <SeverityLabel severity={alert.severity} />
                </Table.Td>
                <Table.Td>
                  <Anchor component={Link} to={`/hosts/${alert.hostId}`} fz="sm">
                    {alert.title}
                  </Anchor>
                </Table.Td>
                <Table.Td className="argus-data">{formatAgo(alert.firedAt, now)}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      ) : (
        <Group gap={8} p="md" c="dimmed">
          <IconCircleCheck size={18} color="var(--mantine-color-healthy-filled)" aria-hidden />
          <Text fz="sm" c="dimmed">
            {alerts.isPending ? "Loading…" : "Nothing needs attention."}
          </Text>
        </Group>
      )}
    </Section>
  );
}
