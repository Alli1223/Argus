import {
  Group,
  Paper,
  SimpleGrid,
  Skeleton,
  Stack,
  Text,
  Title,
  useComputedColorScheme,
} from "@mantine/core";
import { useMemo } from "react";
import { useSearchParams } from "react-router";
import { useFleetMetrics } from "../../api/metrics";
import type { FleetMetrics, HostSummary } from "../../api/types";
import { RangePicker } from "../../components/charts/RangePicker";
import { TimeSeriesChart } from "../../components/charts/TimeSeriesChart";
import { Gauge } from "../../components/gauges/Gauge";
import { trafficColors } from "../../components/watch/traffic";
import { formatBytes } from "../../lib/format";
import { severityColor, severityOf } from "../../lib/severity";
import { describeBucket, rangeFromParams, rangeToParams, type TimeRange } from "../../lib/timeRange";
import { MAX_HOST_LINES, cpuSeries, diskColors, diskSeries, fleetNow, networkSeries, peak } from "./fleet";

/** "1.2 MB/s" as its number and its unit, for the middle of a dial. */
function splitRate(bytesPerSecond: number | null): { value: string; unit: string } {
  const [value, unit = "B"] = formatBytes(bytesPerSecond).split(" ");
  return { value, unit: `${unit}/s` };
}

function percentGauge(label: string, value: number | null, caption: string) {
  const color = severityColor[severityOf(value)];
  return (
    <Gauge
      label={label}
      fraction={value == null ? null : value / 100}
      value={value == null ? "–" : value.toFixed(1)}
      unit="%"
      color={`var(--mantine-color-${color}-filled)`}
      trackColor={`var(--mantine-color-${color}-light)`}
      min="0"
      max="100%"
      caption={caption}
    />
  );
}

/** A traffic dial: the reading now against the busiest moment in the range on show. */
function rateGauge(
  label: string,
  value: number | null,
  history: (number | null)[] | undefined,
  color: string,
  caption: string,
) {
  const top = Math.max(peak(history) ?? 0, value ?? 0);
  const { value: number, unit } = splitRate(value);
  return (
    <Gauge
      label={label}
      fraction={value == null || top === 0 ? null : value / top}
      value={number}
      unit={unit}
      color={color}
      trackColor={`${color}33`}
      min="0"
      max={top > 0 ? `peak ${formatBytes(top, 0)}/s` : ""}
      caption={caption}
    />
  );
}

/**
 * Every system together: average CPU and memory and total traffic and disk activity right now as dials,
 * and CPU, network and disk over time as charts.
 */
export function FleetPanel({ hosts }: { hosts: HostSummary[] }) {
  const [params, setParams] = useSearchParams();
  const range = rangeFromParams(params);
  const fleet = useFleetMetrics(range);
  const scheme = useComputedColorScheme("light");
  const traffic = trafficColors(scheme);
  const disk = diskColors(scheme);
  const now = fleetNow(hosts);

  const changeRange = (next: TimeRange) => setParams(rangeToParams(next), { replace: true });
  const zoom = (from: number, to: number) =>
    setParams(rangeToParams({ from: Math.floor(from), to: Math.ceil(to) }));

  const systems = `${now.reporting} online ${now.reporting === 1 ? "system" : "systems"}`;
  const totals = fleet.data?.totals;

  return (
    <section aria-labelledby="fleet-title">
      <Group justify="space-between" align="center" mb="sm" gap="sm" wrap="wrap">
        <Title id="fleet-title" order={2} fz={17}>
          All systems
        </Title>
        <RangePicker range={range} onChange={changeRange}>
          {totals && totals.time.length > 0 && (
            <Text fz="xs" c="dimmed">
              One point per {describeBucket(totals.bucketSeconds)}.
            </Text>
          )}
        </RangePicker>
      </Group>

      <Paper className="argus-surface" radius="lg" p="lg" mb="md">
        <SimpleGrid cols={{ base: 2, sm: 3, xl: 6 }} spacing="lg">
          {percentGauge("Average CPU", now.cpu, `Per system, of ${systems}`)}
          {percentGauge("Average memory", now.memory, `Per system, of ${systems}`)}
          {rateGauge("Received", now.received, totals?.series.netRx, traffic.received, `Total of ${systems}`)}
          {rateGauge("Sent", now.sent, totals?.series.netTx, traffic.sent, `Total of ${systems}`)}
          {rateGauge("Disk read", now.diskRead, totals?.series.diskRead, disk.read, `Total of ${systems}`)}
          {rateGauge(
            "Disk written",
            now.diskWrite,
            totals?.series.diskWrite,
            disk.write,
            `Total of ${systems}`,
          )}
        </SimpleGrid>
      </Paper>

      {fleet.isPending ? (
        <Stack gap="md">
          <Skeleton h={296} radius="sm" />
          <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="md">
            <Skeleton h={296} radius="sm" />
            <Skeleton h={296} radius="sm" />
          </SimpleGrid>
        </Stack>
      ) : fleet.data ? (
        <FleetCharts fleet={fleet.data} scheme={scheme} refreshing={fleet.isPlaceholderData} onZoom={zoom} />
      ) : (
        <Text fz="sm" c="dimmed">
          The history did not load: {fleet.error?.message}
        </Text>
      )}
    </section>
  );
}

interface FleetChartsProps {
  fleet: FleetMetrics;
  scheme: "light" | "dark";
  refreshing: boolean;
  onZoom: (from: number, to: number) => void;
}

function FleetCharts({ fleet, scheme, refreshing, onZoom }: FleetChartsProps) {
  const cpu = useMemo(() => cpuSeries(fleet, scheme), [fleet, scheme]);
  const network = useMemo(() => networkSeries(fleet, scheme), [fleet, scheme]);
  const disk = useMemo(() => diskSeries(fleet, scheme), [fleet, scheme]);
  const from = Date.parse(fleet.totals.from) / 1000;
  const to = Date.parse(fleet.totals.to) / 1000;

  // CPU has a line per system, so it gets the full width; network and disk share the row below.
  return (
    <Stack gap="md">
      <TimeSeriesChart
        title="CPU across all systems"
        description={
          fleet.hosts.length > MAX_HOST_LINES
            ? "The average, and the busiest system at each moment"
            : "The average, and each system"
        }
        time={fleet.totals.time}
        series={cpu}
        unit="percent"
        max={100}
        from={from}
        to={to}
        refreshing={refreshing}
        onZoom={onZoom}
      />
      <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="md">
        <TimeSeriesChart
          title="Network across all systems"
          description="Traffic in and out of every system, added together"
          time={fleet.totals.time}
          series={network}
          unit="bytesPerSecond"
          from={from}
          to={to}
          refreshing={refreshing}
          onZoom={onZoom}
        />
        <TimeSeriesChart
          title="Disk across all systems"
          description="Reads and writes of every system's disks, added together"
          time={fleet.totals.time}
          series={disk}
          unit="bytesPerSecond"
          from={from}
          to={to}
          refreshing={refreshing}
          onZoom={onZoom}
        />
      </SimpleGrid>
    </Stack>
  );
}
