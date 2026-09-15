import {
  Anchor,
  Box,
  Button,
  Group,
  Pagination,
  SegmentedControl,
  Select,
  Skeleton,
  Table,
  Text,
} from "@mantine/core";
import { notifications } from "@mantine/notifications";
import { IconCircleCheck } from "@tabler/icons-react";
import { Link, useSearchParams } from "react-router";
import { useAcknowledgeAlert, useAlertCounts, useAlerts } from "../../api/alerts";
import { useHosts } from "../../api/hosts";
import type { Alert, AlertCounts, AlertSeverity } from "../../api/types";
import { PageHeader } from "../../components/PageHeader";
import { ErrorScreen } from "../../components/Screens";
import { SeverityLabel } from "../../components/SeverityLabel";
import { formatMetricValue } from "../../lib/alertMetrics";
import { formatAgo, formatDuration } from "../../lib/format";
import { useNow } from "../../lib/useNow";
import { viewFromParams, viewToParams, type AlertsView, type StatusChoice } from "./alertFilters";

const PAGE_SIZE = 50;
const dateTime = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" });

const STATUS_CHOICES: { label: string; value: StatusChoice }[] = [
  { label: "Firing", value: "Firing" },
  { label: "Resolved", value: "Resolved" },
  { label: "All", value: "all" },
];

function firingSentence(counts: AlertCounts): string {
  const parts: [number, string][] = [
    [counts.critical, "critical"],
    [counts.warning, "warning"],
    [counts.info, "info"],
  ];
  const total = counts.critical + counts.warning + counts.info;
  if (total === 0) return "Nothing is firing right now.";
  const detail = parts
    .filter(([count]) => count > 0)
    .map(([count, word]) => `${count} ${word}`)
    .join(", ");
  return `${total} firing: ${detail}.`;
}

export function AlertsPage() {
  const [params, setParams] = useSearchParams();
  const view = viewFromParams(params);
  const alerts = useAlerts({
    status: view.status === "all" ? undefined : view.status,
    severity: view.severity ?? undefined,
    hostId: view.hostId ?? undefined,
    page: view.page,
    pageSize: PAGE_SIZE,
  });
  const counts = useAlertCounts();
  const hosts = useHosts();
  const now = useNow();

  // Any change of filter starts again from the first page.
  const change = (next: Partial<AlertsView>) =>
    setParams(viewToParams({ ...view, page: 1, ...next }), { replace: true });

  if (alerts.isError && !alerts.data) {
    return <ErrorScreen error={alerts.error} onRetry={() => void alerts.refetch()} />;
  }

  const filtered = view.status !== "Firing" || view.severity !== null || view.hostId !== null;
  const pages = alerts.data ? Math.ceil(alerts.data.total / PAGE_SIZE) : 0;
  const hostOptions = (hosts.data ?? []).map((host) => ({ value: host.id, label: host.displayName }));

  return (
    <>
      <PageHeader title="Alerts" description={counts.data ? firingSentence(counts.data) : undefined} />

      <Group gap="sm" mb="md" wrap="wrap">
        <SegmentedControl
          aria-label="Which alerts"
          value={view.status}
          onChange={(status) => change({ status: status as StatusChoice })}
          data={STATUS_CHOICES}
        />
        <Select
          aria-label="Severity"
          placeholder="Any severity"
          data={["Critical", "Warning", "Info"]}
          value={view.severity}
          onChange={(severity) => change({ severity: severity as AlertSeverity | null })}
          clearable
          w={170}
        />
        <Select
          aria-label="Host"
          placeholder="Any host"
          data={hostOptions}
          value={view.hostId}
          onChange={(hostId) => change({ hostId })}
          clearable
          searchable
          nothingFoundMessage="No host by that name"
          w={220}
        />
      </Group>

      <Box
        className="argus-surface"
        style={{
          borderRadius: "var(--mantine-radius-sm)",
          overflowX: "auto",
          opacity: alerts.isPlaceholderData ? 0.55 : 1,
          transition: "opacity 150ms ease",
        }}
      >
        <Table miw={920}>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Severity</Table.Th>
              <Table.Th>Alert</Table.Th>
              <Table.Th>Reading</Table.Th>
              <Table.Th>Started</Table.Th>
              <Table.Th>State</Table.Th>
              <Table.Th>Acknowledged</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {alerts.isPending &&
              Array.from({ length: 3 }, (_, index) => (
                <Table.Tr key={index}>
                  <Table.Td colSpan={6}>
                    <Skeleton h={28} />
                  </Table.Td>
                </Table.Tr>
              ))}
            {alerts.data?.items.map((alert) => (
              <AlertRow key={alert.id} alert={alert} now={now} />
            ))}
            {alerts.data && alerts.data.items.length === 0 && (
              <Table.Tr>
                <Table.Td colSpan={6}>
                  {filtered ? (
                    <Group gap="sm" py="md" justify="center">
                      <Text c="dimmed" fz="sm">
                        No alerts match these filters.
                      </Text>
                      <Button
                        variant="subtle"
                        size="compact-sm"
                        onClick={() => setParams({}, { replace: true })}
                      >
                        Clear filters
                      </Button>
                    </Group>
                  ) : (
                    <Group gap="xs" py="md" justify="center">
                      <IconCircleCheck size={18} color="var(--mantine-color-healthy-filled)" aria-hidden />
                      <Text fz="sm">Nothing is firing.</Text>
                      <Anchor component={Link} to="/rules" fz="sm">
                        See the alert rules
                      </Anchor>
                    </Group>
                  )}
                </Table.Td>
              </Table.Tr>
            )}
          </Table.Tbody>
        </Table>
      </Box>

      {pages > 1 && (
        <Pagination total={pages} value={view.page} onChange={(page) => change({ page })} mt="md" />
      )}
    </>
  );
}

/** The reading that fired the alert against the rule's threshold: "93.2% over 90.0%". */
function Reading({ alert }: { alert: Alert }) {
  if (alert.metric === "HostOffline") return <Text fz="sm">Not reporting</Text>;
  return (
    <Text fz="sm">
      {formatMetricValue(alert.metric, alert.value)}{" "}
      <Text span fz="xs" c="dimmed">
        {alert.operator === "Above" ? "over" : "under"} {formatMetricValue(alert.metric, alert.threshold)}
      </Text>
    </Text>
  );
}

function AlertRow({ alert, now }: { alert: Alert; now: number }) {
  const acknowledge = useAcknowledgeAlert();
  const firing = alert.status === "Firing";
  const lasted = ((alert.resolvedAt ? Date.parse(alert.resolvedAt) : now) - Date.parse(alert.firedAt)) / 1000;

  const acknowledgeAlert = () =>
    acknowledge.mutate(alert.id, {
      onSuccess: () => notifications.show({ color: "healthy", message: "Alert acknowledged." }),
      onError: (error) =>
        notifications.show({
          color: "crimson",
          title: "The alert was not acknowledged",
          message: error.message,
        }),
    });

  return (
    <Table.Tr>
      <Table.Td>
        <SeverityLabel severity={alert.severity} />
      </Table.Td>
      <Table.Td>
        <Anchor component={Link} to={`/hosts/${alert.hostId}`} fz="sm" fw={600}>
          {alert.title}
        </Anchor>
        <Text fz="xs" c="dimmed">
          {alert.ruleName ? `${alert.hostName}, ${alert.ruleName}` : alert.hostName}
        </Text>
      </Table.Td>
      <Table.Td>
        <Reading alert={alert} />
      </Table.Td>
      <Table.Td title={dateTime.format(new Date(alert.firedAt))}>{formatAgo(alert.firedAt, now)}</Table.Td>
      <Table.Td>
        {firing ? (
          <Text fz="sm" fw={600}>
            Firing for {formatDuration(lasted)}
          </Text>
        ) : (
          <Text fz="sm" c="dimmed">
            Resolved after {formatDuration(lasted)}
          </Text>
        )}
      </Table.Td>
      <Table.Td>
        {alert.acknowledgedAt ? (
          <Text fz="sm">
            {alert.acknowledgedBy ?? "Someone"}, {formatAgo(alert.acknowledgedAt, now)}
          </Text>
        ) : firing ? (
          <Button
            variant="light"
            size="compact-sm"
            loading={acknowledge.isPending}
            onClick={acknowledgeAlert}
          >
            Acknowledge
          </Button>
        ) : (
          <Text fz="sm" c="dimmed">
            –
          </Text>
        )}
      </Table.Td>
    </Table.Tr>
  );
}
