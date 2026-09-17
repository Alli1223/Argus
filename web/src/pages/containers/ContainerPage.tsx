import {
  Anchor,
  Box,
  Button,
  Code,
  Group,
  Paper,
  SimpleGrid,
  Skeleton,
  Stack,
  Table,
  Text,
  Title,
  VisuallyHidden,
  useComputedColorScheme,
} from "@mantine/core";
import {
  IconAlertTriangle,
  IconArrowDown,
  IconArrowLeft,
  IconArrowUp,
  IconCircleCheck,
} from "@tabler/icons-react";
import { useMemo, type ReactNode } from "react";
import { Link, useParams, useSearchParams } from "react-router";
import { ApiError } from "../../api/client";
import { useContainer, useContainerHistory } from "../../api/containers";
import type { ContainerDetail, MetricSeries } from "../../api/types";
import { SERIES_COLORS } from "../../components/charts/chartPalette";
import { RangePicker } from "../../components/charts/RangePicker";
import { TimeSeriesChart } from "../../components/charts/TimeSeriesChart";
import { ContainerStatusBadge } from "../../components/containers/ContainerStatusBadge";
import { ErrorScreen, MessageScreen } from "../../components/Screens";
import { Section } from "../../components/Section";
import { UsageMeter } from "../../components/UsageMeter";
import { composeName, containerStatus, describeEvent, isBadEvent } from "../../lib/containers";
import { formatBytes, formatDuration, formatRate } from "../../lib/format";
import { describeBucket, rangeFromParams, rangeToParams, type TimeRange } from "../../lib/timeRange";
import { useNow } from "../../lib/useNow";
import classes from "../host/HostPage.module.css";

const dateTime = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "medium" });

/** One container: its state, what it is, what it used over time and what happened to it. */
export function ContainerPage() {
  const { hostId = "", name = "" } = useParams();
  const detail = useContainer(hostId, name);
  const now = useNow();

  if (detail.isError && !detail.data) {
    if (detail.error instanceof ApiError && detail.error.status === 404) {
      return (
        <MessageScreen
          title="Container not found"
          action={
            <Button component={Link} to={`/hosts/${hostId}`}>
              Back to the host
            </Button>
          }
        >
          It may have been removed, or its host reports to another account.
        </MessageScreen>
      );
    }
    return <ErrorScreen error={detail.error} onRetry={() => void detail.refetch()} />;
  }

  if (!detail.data) {
    return (
      <Stack gap="lg">
        <Skeleton h={64} w={320} />
        <Skeleton h={200} radius="lg" />
      </Stack>
    );
  }

  return <ContainerView detail={detail.data} now={now} />;
}

function ContainerView({ detail, now }: { detail: ContainerDetail; now: number }) {
  const [params, setParams] = useSearchParams();
  const range = rangeFromParams(params);
  const container = detail.container;
  const status = containerStatus(container);
  const history = useContainerHistory(detail.hostId, container.name, range);

  const changeRange = (next: TimeRange) => setParams(rangeToParams(next), { replace: true });
  const zoom = (from: number, to: number) =>
    setParams(rangeToParams({ from: Math.floor(from), to: Math.ceil(to) }));

  return (
    <>
      <Stack gap={6} mb="lg" miw={0}>
        <Anchor component={Link} to={`/hosts/${detail.hostId}`} fz="sm" w="fit-content">
          <Group gap={4} wrap="nowrap">
            <IconArrowLeft size={14} aria-hidden />
            {detail.hostName}
          </Group>
        </Anchor>
        <Group gap="sm" wrap="wrap" align="center">
          <Title order={1} fz={26} style={{ overflowWrap: "anywhere" }}>
            {container.name}
          </Title>
          <ContainerStatusBadge container={container} />
        </Group>
        <Text c="dimmed" fz="sm">
          {[composeName(container), container.image].filter(Boolean).join(", ")}
        </Text>
        {status.reason && (
          <Text c="crimson" fz="sm">
            {status.reason}
          </Text>
        )}
      </Stack>

      <Paper className="argus-surface" radius="lg" p="lg">
        <Group align="flex-start" gap="xl" wrap="wrap">
          <Box className={classes.column}>
            <Title order={2} fz={15} mb="sm">
              {container.state === "running" ? "Right now" : "Last run"}
            </Title>
            <dl className={classes.readings}>
              <Reading label="State">
                {status.label} for {formatDuration((now - Date.parse(container.stateSince)) / 1000)}
              </Reading>
              {container.usage && (
                <>
                  <Reading label="CPU">
                    <UsageMeter value={container.usage.cpuPercent} label="CPU" width={120} />
                  </Reading>
                  <Reading label="Memory">
                    {formatBytes(container.usage.memoryBytes)}
                    {container.usage.memoryLimitBytes != null && (
                      <Text span fz="xs" c="dimmed">
                        {" "}
                        of {formatBytes(container.usage.memoryLimitBytes)}
                      </Text>
                    )}
                  </Reading>
                  <Reading label="Network">
                    {container.usage.netRxBytesPerSec == null ? (
                      <Text span fz="sm" c="dimmed">
                        Shares the host's network
                      </Text>
                    ) : (
                      <Group gap={12} wrap="nowrap" className="argus-data">
                        <Group gap={2} wrap="nowrap">
                          <IconArrowDown size={13} aria-hidden />
                          <VisuallyHidden>Received</VisuallyHidden>
                          {formatRate(container.usage.netRxBytesPerSec)}
                        </Group>
                        <Group gap={2} wrap="nowrap">
                          <IconArrowUp size={13} aria-hidden />
                          <VisuallyHidden>Sent</VisuallyHidden>
                          {formatRate(container.usage.netTxBytesPerSec)}
                        </Group>
                      </Group>
                    )}
                  </Reading>
                </>
              )}
              <Reading label="Restarts">
                {container.restartCount}
                {container.restartsLastHour > 0 && (
                  <Text span fz="xs" c="dimmed">
                    , {container.restartsLastHour} in the last hour
                  </Text>
                )}
              </Reading>
              {container.startedAt && (
                <Reading label="Started">{dateTime.format(new Date(container.startedAt))}</Reading>
              )}
              {container.state !== "running" && container.finishedAt && (
                <Reading label="Stopped">
                  {dateTime.format(new Date(container.finishedAt))}
                  {container.exitCode != null && `, exit code ${container.exitCode}`}
                  {container.oomKilled && ", out of memory"}
                </Reading>
              )}
            </dl>
          </Box>

          <Box className={classes.column}>
            <Title order={2} fz={15} mb="sm">
              Container
            </Title>
            <dl className={classes.readings}>
              <Reading label="Image">{container.image}</Reading>
              <Reading label="ID">
                <Code title={container.id}>{container.id.slice(0, 12)}</Code>
              </Reading>
              {composeName(container) && <Reading label="Compose">{composeName(container)}</Reading>}
              <Reading label="Restart policy">{container.restartPolicy ?? "Unknown"}</Reading>
              <Reading label="Ports">
                {container.ports.length > 0 ? container.ports.join(", ") : "None published"}
              </Reading>
              <Reading label="Created">{dateTime.format(new Date(container.createdAt))}</Reading>
            </dl>
          </Box>
        </Group>
      </Paper>

      <Title order={2} fz={17} mt="xl" mb="sm">
        History
      </Title>
      <RangePicker range={range} onChange={changeRange}>
        {history.data && (
          <Text fz="xs" c="dimmed">
            One point per {describeBucket(history.data.bucketSeconds)}. Drag across a chart to zoom in.
          </Text>
        )}
      </RangePicker>
      <ContainerCharts history={history.data} refreshing={history.isPlaceholderData} onZoom={zoom} />

      <Section title="Events" mt="xl">
        {detail.events.length === 0 ? (
          <Text fz="sm" c="dimmed" p="md">
            Nothing has happened to this container since Argus started watching it.
          </Text>
        ) : (
          <Table miw={480}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>When</Table.Th>
                <Table.Th>What happened</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {detail.events.map((event, index) => (
                <Table.Tr key={`${event.time}-${event.kind}-${index}`}>
                  <Table.Td>{dateTime.format(new Date(event.time))}</Table.Td>
                  <Table.Td>
                    <Group gap={6} wrap="nowrap">
                      {isBadEvent(event) ? (
                        <IconAlertTriangle size={15} color="var(--mantine-color-crimson-text)" aria-hidden />
                      ) : (
                        <IconCircleCheck size={15} color="var(--mantine-color-dimmed)" aria-hidden />
                      )}
                      {describeEvent(event)}
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}
      </Section>
    </>
  );
}

function Reading({ label, children }: { label: string; children: ReactNode }) {
  return (
    <>
      <dt>{label}</dt>
      <dd>{children}</dd>
    </>
  );
}

interface ContainerChartsProps {
  history: MetricSeries | undefined;
  refreshing: boolean;
  onZoom: (from: number, to: number) => void;
}

function ContainerCharts({ history, refreshing, onZoom }: ContainerChartsProps) {
  const scheme = useComputedColorScheme("light");
  const charts = useMemo(() => {
    if (!history) return [];
    const [first, second] = SERIES_COLORS[scheme];
    const pick = (key: string) => history.series[key] ?? history.time.map(() => null);
    return [
      {
        key: "cpu",
        title: "CPU",
        description: "Share of all the host's cores",
        unit: "percent" as const,
        series: [
          { key: "cpu", label: "Average", color: first, values: pick("cpu") },
          { key: "cpuMax", label: "Peak", color: second, values: pick("cpuMax") },
        ],
      },
      {
        key: "memory",
        title: "Memory",
        description: "In use, not counting file cache",
        unit: "bytes" as const,
        series: [{ key: "memory", label: "Memory", color: first, values: pick("memory") }],
      },
      {
        key: "network",
        title: "Network",
        description: "Traffic on the container's own networks",
        unit: "bytesPerSecond" as const,
        series: [
          { key: "netRx", label: "Received", color: first, values: pick("netRx") },
          { key: "netTx", label: "Sent", color: second, values: pick("netTx") },
        ],
      },
    ];
  }, [history, scheme]);

  if (!history) {
    return (
      <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="md" mt="md">
        {Array.from({ length: 3 }, (_, index) => (
          <Skeleton key={index} h={296} radius="sm" />
        ))}
      </SimpleGrid>
    );
  }

  const from = Date.parse(history.from) / 1000;
  const to = Date.parse(history.to) / 1000;
  return (
    <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="md" mt="md">
      {charts.map(({ key, ...chart }) => (
        <TimeSeriesChart
          key={key}
          {...chart}
          time={history.time}
          from={from}
          to={to}
          refreshing={refreshing}
          onZoom={onZoom}
        />
      ))}
    </SimpleGrid>
  );
}
