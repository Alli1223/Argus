import {
  Box,
  Button,
  Group,
  Paper,
  SimpleGrid,
  Skeleton,
  Stack,
  Text,
  Title,
  VisuallyHidden,
  useComputedColorScheme,
} from "@mantine/core";
import { IconArrowDown, IconArrowUp } from "@tabler/icons-react";
import { useMemo, type ReactNode } from "react";
import { Link, useParams, useSearchParams } from "react-router";
import { ApiError } from "../../api/client";
import { useHost } from "../../api/hosts";
import { useHostMetrics } from "../../api/metrics";
import type { HostDetail, MetricSeries } from "../../api/types";
import { RangePicker } from "../../components/charts/RangePicker";
import { TimeSeriesChart } from "../../components/charts/TimeSeriesChart";
import { AgentVersion } from "../../components/AgentVersion";
import { ErrorScreen, MessageScreen } from "../../components/Screens";
import { UsageMeter } from "../../components/UsageMeter";
import { EyeGlyph } from "../../components/watch/Watch";
import { formatAgo, formatBytes, formatDuration, formatRate } from "../../lib/format";
import { describeBucket, rangeFromParams, rangeToParams, type TimeRange } from "../../lib/timeRange";
import { useNow } from "../../lib/useNow";
import classes from "./HostPage.module.css";
import { hostCharts } from "./hostCharts";
import { HostHeader } from "./HostHeader";
import {
  ContainersSection,
  FilesystemsSection,
  InterfacesSection,
  ProcessesSection,
  ServicesSection,
  TemperaturesSection,
} from "./HostResources";

const dateTime = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" });

export function HostPage() {
  const { hostId = "" } = useParams();
  const host = useHost(hostId);
  const now = useNow();

  if (host.isError) {
    if (host.error instanceof ApiError && host.error.status === 404) {
      return (
        <MessageScreen
          title="Host not found"
          action={
            <Button component={Link} to="/hosts">
              Back to hosts
            </Button>
          }
        >
          It may have been deleted, or it reports to another account.
        </MessageScreen>
      );
    }
    return <ErrorScreen error={host.error} onRetry={() => void host.refetch()} />;
  }

  if (host.isPending) {
    return (
      <Stack gap="lg">
        <Skeleton h={64} w={320} />
        <Skeleton h={230} radius="lg" />
      </Stack>
    );
  }

  return <HostView host={host.data} now={now} />;
}

function HostView({ host, now }: { host: HostDetail; now: number }) {
  const [params, setParams] = useSearchParams();
  const range = rangeFromParams(params);
  const metrics = useHostMetrics(host.id, range);

  const changeRange = (next: TimeRange) => setParams(rangeToParams(next), { replace: true });
  // Zooming adds a history entry, so Back returns to the wider view.
  const zoom = (from: number, to: number) =>
    setParams(rangeToParams({ from: Math.floor(from), to: Math.ceil(to) }));

  return (
    <>
      <HostHeader host={host} />
      <NowPanel host={host} now={now} />
      <ServicesSection hostId={host.id} now={now} />
      <ContainersSection hostId={host.id} now={now} />
      <ProcessesSection hostId={host.id} now={now} />

      <Title order={2} fz={17} mt="xl" mb="sm">
        History
      </Title>
      <RangePicker range={range} onChange={changeRange}>
        {metrics.data && (
          <Text fz="xs" c="dimmed">
            One point per {describeBucket(metrics.data.bucketSeconds)}. Drag across a chart to zoom in.
          </Text>
        )}
      </RangePicker>

      {metrics.isError && !metrics.data ? (
        <Group gap="sm" mt="md">
          <Text fz="sm">The history did not load: {metrics.error.message}</Text>
          <Button variant="light" size="compact-sm" onClick={() => void metrics.refetch()}>
            Try again
          </Button>
        </Group>
      ) : (
        <HostCharts host={host} metrics={metrics.data} refreshing={metrics.isPlaceholderData} onZoom={zoom} />
      )}
      <TemperaturesSection hostId={host.id} range={range} onZoom={zoom} />
      <FilesystemsSection hostId={host.id} range={range} />
      <InterfacesSection hostId={host.id} range={range} onZoom={zoom} />
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

/** The host's eye beside its newest readings and what the machine is. */
function NowPanel({ host, now }: { host: HostDetail; now: number }) {
  const latest = host.latest;
  const offline = host.status === "Offline";

  return (
    <Paper className="argus-surface" radius="lg" p="lg">
      <Group align="flex-start" gap="xl" wrap="wrap">
        <Stack gap={6} align="center" w={150}>
          <EyeGlyph host={host} size={132} />
          <Text fz="xs" c="dimmed" ta="center">
            {offline ? "Stopped reporting" : "Last report"} {formatAgo(host.lastSeenAt, now)}
          </Text>
        </Stack>

        <Box className={classes.column}>
          <Title order={2} fz={15} mb="sm">
            {offline ? "Last readings" : "Right now"}
          </Title>
          {latest ? (
            <dl className={classes.readings}>
              <Reading label="CPU">
                <UsageMeter value={latest.cpuPercent} label="CPU" width={120} />
              </Reading>
              <Reading label="Memory">
                <Group gap="xs" wrap="wrap">
                  <UsageMeter value={latest.memoryPercent} label="Memory" width={120} />
                  <Text fz="xs" c="dimmed">
                    {formatBytes(latest.memoryUsedBytes)} of {formatBytes(latest.memoryTotalBytes)}
                  </Text>
                </Group>
              </Reading>
              {latest.swapPercent != null && (
                <Reading label="Swap">
                  <UsageMeter value={latest.swapPercent} label="Swap" width={120} />
                </Reading>
              )}
              <Reading label="Fullest disk">
                <UsageMeter value={latest.diskUsedPercent} label="Fullest disk" width={120} />
              </Reading>
              {latest.load1 != null && (
                <Reading label="Load">
                  <span className="argus-data">{latest.load1.toFixed(2)}</span>{" "}
                  <Text span fz="xs" c="dimmed">
                    over the last minute
                  </Text>
                </Reading>
              )}
              <Reading label="Network">
                <Group gap={12} wrap="nowrap" className="argus-data">
                  <Group gap={2} wrap="nowrap">
                    <IconArrowDown size={13} aria-hidden />
                    <VisuallyHidden>Received</VisuallyHidden>
                    {formatRate(latest.netRxBytesPerSec)}
                  </Group>
                  <Group gap={2} wrap="nowrap">
                    <IconArrowUp size={13} aria-hidden />
                    <VisuallyHidden>Sent</VisuallyHidden>
                    {formatRate(latest.netTxBytesPerSec)}
                  </Group>
                </Group>
              </Reading>
              <Reading label="Up for">
                <span className="argus-data">{formatDuration(latest.uptimeSeconds)}</span>
              </Reading>
            </dl>
          ) : (
            <Text fz="sm" c="dimmed">
              No readings yet. They appear within a minute of the agent starting.
            </Text>
          )}
        </Box>

        <Box className={classes.column}>
          <Title order={2} fz={15} mb="sm">
            System
          </Title>
          <dl className={classes.readings}>
            <Reading label="Processor">{host.cpuModel ?? "Unknown"}</Reading>
            <Reading label="Cores">
              {host.cpuCores
                ? `${host.cpuCores} cores, ${host.cpuLogicalProcessors} threads`
                : `${host.cpuLogicalProcessors} threads`}
            </Reading>
            <Reading label="Memory">{formatBytes(host.memoryTotalBytes)}</Reading>
            <Reading label="Addresses">
              {host.ipAddresses.length > 0 ? host.ipAddresses.join(", ") : "None reported"}
            </Reading>
            <Reading label="Booted">
              {host.bootTime ? dateTime.format(new Date(host.bootTime)) : "Unknown"}
            </Reading>
            <Reading label="Agent">
              <AgentVersion host={host} now={now} />
            </Reading>
            <Reading label="Added">{dateTime.format(new Date(host.createdAt))}</Reading>
          </dl>
        </Box>
      </Group>
    </Paper>
  );
}

interface HostChartsProps {
  host: HostDetail;
  metrics: MetricSeries | undefined;
  refreshing: boolean;
  onZoom: (from: number, to: number) => void;
}

function HostCharts({ host, metrics, refreshing, onZoom }: HostChartsProps) {
  const scheme = useComputedColorScheme("light");
  const charts = useMemo(
    () => (metrics ? hostCharts(metrics, scheme, host.platform, host.cpuLogicalProcessors) : []),
    [metrics, scheme, host.platform, host.cpuLogicalProcessors],
  );

  if (!metrics) {
    return (
      <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="md" mt="md">
        {Array.from({ length: 4 }, (_, index) => (
          <Skeleton key={index} h={296} radius="sm" />
        ))}
      </SimpleGrid>
    );
  }

  const from = Date.parse(metrics.from) / 1000;
  const to = Date.parse(metrics.to) / 1000;
  return (
    <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="md" mt="md">
      {charts.map(({ key, ...chart }) => (
        <TimeSeriesChart
          key={key}
          {...chart}
          time={metrics.time}
          from={from}
          to={to}
          refreshing={refreshing}
          onZoom={onZoom}
        />
      ))}
    </SimpleGrid>
  );
}
