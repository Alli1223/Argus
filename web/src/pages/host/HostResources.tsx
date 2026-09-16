import { Box, Group, SimpleGrid, Skeleton, Table, Text, Title, useComputedColorScheme } from "@mantine/core";
import { IconAlertTriangle, IconCircleCheck } from "@tabler/icons-react";
import { useMemo } from "react";
import {
  useFilesystemHistory,
  useHostFilesystems,
  useHostProcesses,
  useHostServices,
  useNetworkHistory,
  useTemperatureHistory,
} from "../../api/metrics";
import { SERIES_COLORS } from "../../components/charts/chartPalette";
import { TimeSeriesChart } from "../../components/charts/TimeSeriesChart";
import { Section } from "../../components/Section";
import { Sparkline } from "../../components/Sparkline";
import { UsageMeter } from "../../components/UsageMeter";
import { formatAgo, formatBytes, formatDuration, formatPercent } from "../../lib/format";
import { temperatureCharts } from "../../lib/temperatures";
import type { TimeRange } from "../../lib/timeRange";
import { describeTrend, interfaceCharts } from "./hostResources";
import { describeServices } from "./hostServices";

/** The processes using the most CPU when the host last reported. */
export function ProcessesSection({ hostId, now }: { hostId: string; now: number }) {
  const processes = useHostProcesses(hostId);
  const snapshot = processes.data;
  const rows = snapshot ? [...snapshot.processes].sort((a, b) => b.cpuPercent - a.cpuPercent) : [];

  return (
    <Section
      title="Busiest processes"
      mt="xl"
      action={
        snapshot && (
          <Text fz="xs" c="dimmed">
            As of {formatAgo(snapshot.capturedAt, now)}
          </Text>
        )
      }
    >
      {processes.isPending ? (
        <Skeleton h={120} />
      ) : rows.length === 0 ? (
        <Text fz="sm" c="dimmed" p="md">
          No process list yet. The agent sends one with its readings.
        </Text>
      ) : (
        <Table miw={560}>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Process</Table.Th>
              <Table.Th ta="right">PID</Table.Th>
              <Table.Th>CPU</Table.Th>
              <Table.Th ta="right">Memory</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {rows.map((process) => (
              <Table.Tr key={process.pid}>
                <Table.Td>
                  <Box maw={420}>
                    <Text fz="sm" truncate="end" title={process.name}>
                      {process.name}
                    </Text>
                  </Box>
                </Table.Td>
                <Table.Td ta="right">{process.pid}</Table.Td>
                <Table.Td>
                  <UsageMeter value={process.cpuPercent} label={`${process.name} CPU`} />
                </Table.Td>
                <Table.Td ta="right">{formatBytes(process.memoryBytes)}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      )}
    </Section>
  );
}

/** Every filesystem's space now, with how its use moved over the chosen range. */
export function FilesystemsSection({ hostId, range }: { hostId: string; range: TimeRange }) {
  const scheme = useComputedColorScheme("light");
  const filesystems = useHostFilesystems(hostId);
  const history = useFilesystemHistory(hostId, range);
  const rows = [...(filesystems.data ?? [])].sort((a, b) => a.mountPoint.localeCompare(b.mountPoint));

  return (
    <Section title="Filesystems" mt="xl">
      {filesystems.isPending ? (
        <Skeleton h={120} />
      ) : rows.length === 0 ? (
        <Text fz="sm" c="dimmed" p="md">
          No filesystems reported in the last day.
        </Text>
      ) : (
        <Table miw={760}>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Mounted at</Table.Th>
              <Table.Th>Used</Table.Th>
              <Table.Th>Space</Table.Th>
              <Table.Th ta="right">Free</Table.Th>
              <Table.Th ta="right">Inodes used</Table.Th>
              <Table.Th>Trend</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {rows.map((filesystem) => {
              const trend = history.data?.series[filesystem.mountPoint] ?? [];
              const inodes =
                filesystem.inodesTotal && filesystem.inodesUsed != null
                  ? (100 * filesystem.inodesUsed) / filesystem.inodesTotal
                  : null;
              return (
                <Table.Tr key={filesystem.mountPoint}>
                  <Table.Td>
                    <Text fz="sm" fw={600}>
                      {filesystem.mountPoint}
                    </Text>
                    <Text fz="xs" c="dimmed">
                      {[filesystem.device, filesystem.fsType].filter(Boolean).join(", ")}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <UsageMeter
                      value={filesystem.usedPercent}
                      label={`${filesystem.mountPoint} used`}
                      width={96}
                    />
                  </Table.Td>
                  <Table.Td>
                    {formatBytes(filesystem.usedBytes)} of {formatBytes(filesystem.totalBytes)}
                  </Table.Td>
                  <Table.Td ta="right">{formatBytes(filesystem.availableBytes)}</Table.Td>
                  <Table.Td ta="right">{formatPercent(inodes)}</Table.Td>
                  <Table.Td style={{ opacity: history.isPlaceholderData ? 0.55 : 1 }}>
                    <Sparkline
                      values={trend}
                      color={SERIES_COLORS[scheme][0]}
                      label={`${filesystem.mountPoint} used, ${describeTrend(trend)}`}
                    />
                  </Table.Td>
                </Table.Tr>
              );
            })}
          </Table.Tbody>
        </Table>
      )}
    </Section>
  );
}

const MAX_INTERFACE_CHARTS = 4;

/**
 * Traffic per interface as small multiples. Hosts with a single interface skip this: the network
 * chart above already is that interface.
 */
export function InterfacesSection({
  hostId,
  range,
  onZoom,
}: {
  hostId: string;
  range: TimeRange;
  onZoom: (from: number, to: number) => void;
}) {
  const scheme = useComputedColorScheme("light");
  const network = useNetworkHistory(hostId, range);
  const charts = useMemo(
    () => (network.data ? interfaceCharts(network.data, scheme) : []),
    [network.data, scheme],
  );

  if (!network.data || charts.length < 2) return null;

  const from = Date.parse(network.data.from) / 1000;
  const to = Date.parse(network.data.to) / 1000;
  return (
    <Box mt="xl">
      <Title order={2} fz={17}>
        Network interfaces
      </Title>
      <Text fz="xs" c="dimmed" mb="sm">
        {charts.length > MAX_INTERFACE_CHARTS
          ? `The ${MAX_INTERFACE_CHARTS} busiest of ${charts.length} interfaces.`
          : "Traffic on each interface."}
      </Text>
      <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="md">
        {charts.slice(0, MAX_INTERFACE_CHARTS).map((chart) => (
          <TimeSeriesChart
            key={chart.name}
            title={chart.name}
            time={network.data.time}
            series={chart.series}
            unit="bytesPerSecond"
            from={from}
            to={to}
            refreshing={network.isPlaceholderData}
            onZoom={onZoom}
          />
        ))}
      </SimpleGrid>
    </Box>
  );
}

/**
 * Every temperature sensor the host reports, one chart per device. Hosts without sensors (most virtual
 * machines) skip this.
 */
export function TemperaturesSection({
  hostId,
  range,
  onZoom,
}: {
  hostId: string;
  range: TimeRange;
  onZoom: (from: number, to: number) => void;
}) {
  const scheme = useComputedColorScheme("light");
  const temperatures = useTemperatureHistory(hostId, range);
  const charts = useMemo(
    () => (temperatures.data ? temperatureCharts(temperatures.data, scheme) : []),
    [temperatures.data, scheme],
  );

  if (!temperatures.data || charts.length === 0) return null;

  const from = Date.parse(temperatures.data.from) / 1000;
  const to = Date.parse(temperatures.data.to) / 1000;
  return (
    <Box mt="xl">
      <Title order={2} fz={17}>
        Temperatures
      </Title>
      <Text fz="xs" c="dimmed" mb="sm">
        The hottest reading of each sensor in each point.
      </Text>
      <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="md">
        {charts.map(({ key, ...chart }) => (
          <TimeSeriesChart
            key={key}
            {...chart}
            time={temperatures.data.time}
            unit="celsius"
            from={from}
            to={to}
            refreshing={temperatures.isPlaceholderData}
            onZoom={onZoom}
          />
        ))}
      </SimpleGrid>
    </Box>
  );
}

const dateTime = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" });

/** Services that should be running but are not, from the host's newest service check. */
export function ServicesSection({ hostId, now }: { hostId: string; now: number }) {
  const services = useHostServices(hostId);
  const status = services.data;
  const failures = status?.failures ?? [];

  return (
    <Section
      title="Services"
      mt="xl"
      action={
        status && (
          <Text fz="xs" c="dimmed">
            {describeServices(status, now)}
          </Text>
        )
      }
    >
      {services.isPending ? (
        <Skeleton h={56} />
      ) : failures.length === 0 ? (
        <Group gap="xs" p="md" wrap="nowrap">
          {status?.checkedAt ? (
            <>
              <IconCircleCheck size={18} color="var(--mantine-color-healthy-filled)" aria-hidden />
              <Text fz="sm">Every service the agent watches is running.</Text>
            </>
          ) : (
            <Text fz="sm" c="dimmed">
              Agents check services about once a minute on machines run by systemd or Windows. No check has
              arrived from this one yet.
            </Text>
          )}
        </Group>
      ) : (
        <Table miw={560}>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Service</Table.Th>
              <Table.Th>State</Table.Th>
              <Table.Th>Failing for</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {failures.map((failure) => (
              <Table.Tr key={failure.service}>
                <Table.Td>
                  <Text fz="sm" fw={600}>
                    {failure.service}
                  </Text>
                  {failure.description && (
                    <Text fz="xs" c="dimmed">
                      {failure.description}
                    </Text>
                  )}
                </Table.Td>
                <Table.Td>
                  <Group gap={6} wrap="nowrap">
                    <IconAlertTriangle size={15} color="var(--mantine-color-crimson-text)" aria-hidden />
                    {failure.state}
                  </Group>
                </Table.Td>
                <Table.Td title={`Since ${dateTime.format(new Date(failure.since))}`}>
                  {formatDuration((now - Date.parse(failure.since)) / 1000)}
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      )}
    </Section>
  );
}
