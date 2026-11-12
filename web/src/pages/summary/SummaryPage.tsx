import {
  Anchor,
  Box,
  Group,
  Paper,
  SegmentedControl,
  Select,
  SimpleGrid,
  Skeleton,
  Table,
  Text,
  UnstyledButton,
  useComputedColorScheme,
} from "@mantine/core";
import { IconChevronDown, IconChevronUp } from "@tabler/icons-react";
import { useState, type ReactNode } from "react";
import { Link, useSearchParams } from "react-router";
import { useUsageSummary } from "../../api/summary";
import type { UsagePeriod, UsageStats, UsageSummary } from "../../api/types";
import { BarChart, type BarCategory } from "../../components/charts/BarChart";
import type { ChartSeries } from "../../components/charts/chartData";
import { DISK_COLORS, SERIES_COLORS } from "../../components/charts/chartPalette";
import { PageHeader } from "../../components/PageHeader";
import { ErrorScreen, MessageScreen } from "../../components/Screens";
import { Section } from "../../components/Section";
import { UsageMeter } from "../../components/UsageMeter";
import { formatBytes, formatDuration, formatPercent, formatTemperature } from "../../lib/format";
import {
  combineStats,
  hostUsage,
  machineRanking,
  machineSeries,
  periodDate,
  periodLabel,
  periodOption,
  periodShort,
  PERIOD_OPTIONS,
  sortHostUsage,
  type ByteMetric,
  type PeriodOption,
  type UsageSortKey,
} from "../../lib/summary";

type ByteColumn = "diskReadBytes" | "diskWriteBytes" | "networkReceivedBytes" | "networkSentBytes";

/** Long-term totals: disk and network bytes, CPU, memory, availability and alerts by day, month or year. */
export function SummaryPage() {
  const [params, setParams] = useSearchParams();
  const option = periodOption(params.get("by"));
  const summary = useUsageSummary(option.value, option.count);
  const [selected, setSelected] = useState(0);

  const changePeriod = (value: string) => {
    setSelected(0);
    setParams(value === "Day" ? {} : { by: value.toLowerCase() }, { replace: true });
  };

  if (summary.isError && !summary.data) {
    return <ErrorScreen error={summary.error} onRetry={() => void summary.refetch()} />;
  }

  const data = summary.data?.period === option.value ? summary.data : undefined;

  return (
    <>
      <PageHeader
        title="Summary"
        description="How much every machine read, wrote, downloaded and uploaded, with CPU, memory, availability and alerts, by day, month or year."
      />
      <Group gap="md" mb="lg" wrap="wrap">
        <SegmentedControl
          aria-label="Group by"
          value={option.value}
          onChange={changePeriod}
          data={PERIOD_OPTIONS.map(({ value, label }) => ({ value, label }))}
        />
        {data && (
          <Text fz="xs" c="dimmed">
            Periods start at midnight in {data.timeZone}. Figures come from hourly averages, kept for{" "}
            {data.historyDays} days.
          </Text>
        )}
      </Group>

      {!data ? (
        <Box>
          <SimpleGrid cols={{ base: 2, sm: 3, lg: 6 }} spacing="md">
            {Array.from({ length: 6 }, (_, index) => (
              <Skeleton key={index} h={92} radius="sm" />
            ))}
          </SimpleGrid>
          <Skeleton h={360} radius="sm" mt="xl" />
        </Box>
      ) : data.hosts.length === 0 ? (
        <MessageScreen title="No machines yet">
          Add a system and its totals will build up here as it reports.
        </MessageScreen>
      ) : (
        <SummaryContent
          summary={data}
          option={option}
          selected={Math.min(selected, data.periods.length - 1)}
          onSelect={setSelected}
          refreshing={summary.isPlaceholderData}
        />
      )}
    </>
  );
}

interface SummaryContentProps {
  summary: UsageSummary;
  option: PeriodOption;
  selected: number;
  onSelect: (index: number) => void;
  refreshing: boolean;
}

function SummaryContent({ summary, option, selected, onSelect, refreshing }: SummaryContentProps) {
  // Choosing a row in the fleet table moves the machines to that period too.
  const [choice, setChoice] = useState<string | null>(null);
  const select = (index: number) => {
    setChoice(null);
    onSelect(index);
  };
  const label = (index: number) =>
    periodLabel(summary.period, index, summary.periods[index].start, summary.timeZone);
  const current = summary.periods[selected];
  const previous = summary.periods[selected + 1];

  return (
    <Box style={{ opacity: refreshing ? 0.6 : 1, transition: "opacity 150ms" }}>
      <Text fw={600} fz={17} mb="xs" className="argus-wide">
        {label(selected)}
        {selected < 2 && (
          <Text span c="dimmed" fz="sm" fw={400} ml="xs">
            {periodDate(summary.period, current.start, summary.timeZone)}
          </Text>
        )}
      </Text>
      <Headline
        stats={current.totals}
        previous={previous?.totals}
        previousLabel={previous && label(selected + 1)}
        internet={summary.periods.some((period) => period.internet) ? current.internet : undefined}
        previousInternet={previous?.internet}
      />

      <SummaryCharts
        summary={summary}
        label={label}
        selected={selected}
        onSelect={select}
        refreshing={refreshing}
      />

      <Section title={`All machines, ${option.label.toLowerCase()}`} mt="xl">
        <FleetTable summary={summary} option={option} label={label} selected={selected} onSelect={select} />
      </Section>

      <HostSection
        summary={summary}
        option={option}
        label={label}
        selected={selected}
        choice={choice}
        onChoose={setChoice}
      />
    </Box>
  );
}

interface SummaryChartsProps {
  summary: UsageSummary;
  label: (index: number) => string;
  /** Periods are newest first; the charts run oldest to newest, left to right. */
  selected: number;
  onSelect: (index: number) => void;
  refreshing: boolean;
}

const MACHINE_METRICS: { value: ByteMetric; label: string }[] = [
  { value: "networkReceivedBytes", label: "Downloaded" },
  { value: "networkSentBytes", label: "Uploaded" },
  { value: "diskWriteBytes", label: "Written" },
  { value: "diskReadBytes", label: "Read" },
];

/** Bars of traffic, disk activity, usage and each machine's share over the periods on show. */
function SummaryCharts({ summary, label, selected, onSelect, refreshing }: SummaryChartsProps) {
  const scheme = useComputedColorScheme("light");
  const colors = useByteColors();
  const [metric, setMetric] = useState<ByteMetric>("networkReceivedBytes");

  const count = summary.periods.length;
  const oldestFirst = [...summary.periods].reverse();
  const categories: BarCategory[] = oldestFirst.map((period, index) => ({
    key: period.start,
    label: periodShort(summary.period, period.start, summary.timeZone),
    title: `${label(count - 1 - index)}${count - 1 - index < 2 ? `, ${periodDate(summary.period, period.start, summary.timeZone)}` : ""}`,
  }));
  const pick = (value: (stats: UsageStats) => number | null) =>
    oldestFirst.map((period) => (period.hosts.length === 0 ? null : value(period.totals)));
  const toChart = (index: number) => count - 1 - index;
  const chartProps = {
    categories,
    selected: toChart(selected),
    onSelect: (index: number) => onSelect(toChart(index)),
    refreshing,
  };

  const palette = SERIES_COLORS[scheme];
  const machines: ChartSeries[] = machineSeries(summary, metric).map((series, index) => ({
    ...series,
    color: palette[index],
  }));
  const metricLabel = MACHINE_METRICS.find((item) => item.value === metric)!.label.toLowerCase();

  return (
    <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="md" mt="xl">
      {oldestFirst.some((period) => period.internet) && (
        <Box style={{ gridColumn: "1 / -1" }}>
          <BarChart
            {...chartProps}
            title="Internet"
            description="Data downloaded from and uploaded to the internet, through the UniFi gateway"
            unit="bytes"
            series={[
              {
                key: "down",
                label: "Downloaded",
                color: colors.networkReceivedBytes,
                values: oldestFirst.map((period) => period.internet?.downloadedBytes ?? null),
              },
              {
                key: "up",
                label: "Uploaded",
                color: colors.networkSentBytes,
                values: oldestFirst.map((period) => period.internet?.uploadedBytes ?? null),
              },
            ]}
          />
        </Box>
      )}
      <BarChart
        {...chartProps}
        title="Machine traffic"
        description="Data received and sent by all machines, local network included"
        unit="bytes"
        series={[
          {
            key: "received",
            label: "Downloaded",
            color: colors.networkReceivedBytes,
            values: pick((stats) => stats.networkReceivedBytes),
          },
          {
            key: "sent",
            label: "Uploaded",
            color: colors.networkSentBytes,
            values: pick((stats) => stats.networkSentBytes),
          },
        ]}
      />
      <BarChart
        {...chartProps}
        title="Disk"
        description="Data written to and read from disk by all machines"
        unit="bytes"
        series={[
          {
            key: "write",
            label: "Written",
            color: colors.diskWriteBytes,
            values: pick((stats) => stats.diskWriteBytes),
          },
          {
            key: "read",
            label: "Read",
            color: colors.diskReadBytes,
            values: pick((stats) => stats.diskReadBytes),
          },
        ]}
      />
      <Box style={{ gridColumn: "1 / -1" }}>
        <BarChart
          {...chartProps}
          title="By machine"
          description={`Data ${metricLabel === "read" || metricLabel === "written" ? `${metricLabel} on disk` : metricLabel} by each machine`}
          unit="bytes"
          mode="stacked"
          series={machines}
          actions={
            <SegmentedControl
              size="xs"
              aria-label="Figure by machine"
              value={metric}
              onChange={(value) => setMetric(value as ByteMetric)}
              data={MACHINE_METRICS}
            />
          }
        />
      </Box>
      <BarChart
        {...chartProps}
        title="CPU, memory and availability"
        description="Averages across all machines"
        unit="percent"
        max={100}
        series={[
          { key: "cpu", label: "CPU", color: palette[0], values: pick((stats) => stats.cpuAveragePercent) },
          {
            key: "memory",
            label: "Memory",
            color: palette[1],
            values: pick((stats) => stats.memoryAveragePercent),
          },
          {
            key: "availability",
            label: "Availability",
            color: palette[2],
            values: pick((stats) => stats.availabilityPercent),
          },
        ]}
      />
      <ShareByMachine summary={summary} index={selected} title={label(selected)} />
    </SimpleGrid>
  );
}

/** Each byte figure of one period split by machine, as bars that fill to 100%. */
function ShareByMachine({ summary, index, title }: { summary: UsageSummary; index: number; title: string }) {
  const scheme = useComputedColorScheme("light");
  const palette = SERIES_COLORS[scheme];
  const ranking = machineRanking(summary);
  const period = summary.periods[index];
  const colorOf = (hostId: string) => {
    const rank = ranking.findIndex((host) => host.hostId === hostId);
    return palette[rank < 0 || rank >= palette.length - 1 ? palette.length - 1 : rank];
  };
  const names = new Map(summary.hosts.map((host) => [host.hostId, host.displayName]));

  return (
    <Paper component="figure" className="argus-surface" m={0} p="md" radius="sm" miw={0}>
      <Text fw={600} fz={15} className="argus-wide">
        Share by machine
      </Text>
      <Text fz="xs" c="dimmed">
        Who moved the data, {title.toLowerCase().startsWith("today") ? "today" : title}
      </Text>
      <Box mt="md">
        {MACHINE_METRICS.map((item) => {
          const parts = period.hosts
            .map((usage) => ({ hostId: usage.hostId, value: usage.stats[item.value] ?? 0 }))
            .filter((part) => part.value > 0)
            .sort((a, b) => b.value - a.value);
          const total = parts.reduce((sum, part) => sum + part.value, 0);
          return (
            <Box key={item.value} mb="md">
              <Group justify="space-between" mb={4}>
                <Text fz="sm">{item.label}</Text>
                <Text fz="sm" className="argus-data" c="dimmed">
                  {formatBytes(total || null)}
                </Text>
              </Group>
              <Group
                gap={1}
                wrap="nowrap"
                h={14}
                style={{ borderRadius: 3, overflow: "hidden", background: "var(--argus-border)" }}
                role="img"
                aria-label={`${item.label}: ${
                  parts.map((part) => `${names.get(part.hostId)} ${formatBytes(part.value)}`).join(", ") ||
                  "nothing"
                }`}
              >
                {parts.map((part) => (
                  <Box
                    key={part.hostId}
                    h="100%"
                    title={`${names.get(part.hostId)}: ${formatBytes(part.value)} (${formatPercent((100 * part.value) / total)})`}
                    style={{
                      width: `${(100 * part.value) / total}%`,
                      background: colorOf(part.hostId),
                      flexShrink: 0,
                    }}
                  />
                ))}
              </Group>
            </Box>
          );
        })}
      </Box>
      <Group gap="md" wrap="wrap">
        {period.hosts
          .filter((usage) => ranking.some((host) => host.hostId === usage.hostId))
          .sort(
            (a, b) =>
              ranking.findIndex((host) => host.hostId === a.hostId) -
              ranking.findIndex((host) => host.hostId === b.hostId),
          )
          .map((usage) => (
            <Group key={usage.hostId} gap={6} wrap="nowrap">
              <span
                style={{
                  width: 10,
                  height: 10,
                  borderRadius: 2,
                  background: colorOf(usage.hostId),
                  display: "inline-block",
                }}
                aria-hidden
              />
              <Text fz="xs">{names.get(usage.hostId)}</Text>
            </Group>
          ))}
      </Group>
    </Paper>
  );
}

interface TileProps {
  label: string;
  value: string;
  /** The same figure for the period before, for comparison. */
  note?: ReactNode;
}

function Tile({ label, value, note }: TileProps) {
  return (
    <Paper className="argus-surface" p="md" radius="sm">
      <Text fz="xs" c="dimmed" fw={500}>
        {label}
      </Text>
      <Text fz={22} fw={600} className="argus-data" mt={2}>
        {value}
      </Text>
      {note && (
        <Text fz="xs" c="dimmed" className="argus-data">
          {note}
        </Text>
      )}
    </Paper>
  );
}

interface HeadlineProps {
  stats: UsageStats;
  previous?: UsageStats;
  previousLabel?: string;
  /** The period's internet traffic; undefined hides it, for fleets without a UniFi network. */
  internet?: UsagePeriod["internet"];
  previousInternet?: UsagePeriod["internet"];
}

/** The selected period's fleet totals, each with the period before beside it. */
function Headline({ stats, previous, previousLabel, internet, previousInternet }: HeadlineProps) {
  const before = (text: string) => (previous ? `${previousLabel}: ${text}` : undefined);

  return (
    <SimpleGrid
      cols={internet === undefined ? { base: 2, sm: 3, lg: 6 } : { base: 2, sm: 4, xl: 8 }}
      spacing="md"
    >
      {internet !== undefined && (
        <>
          <Tile
            label="Internet down"
            value={formatBytes(internet?.downloadedBytes)}
            note={before(formatBytes(previousInternet?.downloadedBytes))}
          />
          <Tile
            label="Internet up"
            value={formatBytes(internet?.uploadedBytes)}
            note={before(formatBytes(previousInternet?.uploadedBytes))}
          />
        </>
      )}
      <Tile
        label="Downloaded"
        value={formatBytes(stats.networkReceivedBytes)}
        note={before(formatBytes(previous?.networkReceivedBytes))}
      />
      <Tile
        label="Uploaded"
        value={formatBytes(stats.networkSentBytes)}
        note={before(formatBytes(previous?.networkSentBytes))}
      />
      <Tile
        label="Disk written"
        value={formatBytes(stats.diskWriteBytes)}
        note={before(formatBytes(previous?.diskWriteBytes))}
      />
      <Tile
        label="Disk read"
        value={formatBytes(stats.diskReadBytes)}
        note={before(formatBytes(previous?.diskReadBytes))}
      />
      <Tile
        label="Average CPU"
        value={formatPercent(stats.cpuAveragePercent, 1)}
        note={before(formatPercent(previous?.cpuAveragePercent, 1))}
      />
      <Tile
        label="Alerts"
        value={String(stats.alerts)}
        note={
          stats.criticalAlerts > 0
            ? `${stats.criticalAlerts} critical`
            : before(String(previous?.alerts ?? 0))
        }
      />
    </SimpleGrid>
  );
}

/** A byte count with a bar against the largest in its column, in the column's chart colour. */
function ByteCell({ value, max, color }: { value: number | null; max: number; color: string }) {
  const share = value != null && max > 0 ? Math.max(value / max, value > 0 ? 0.02 : 0) : 0;
  return (
    <Table.Td>
      <Group gap={8} wrap="nowrap">
        <Text fz="sm" w={72} ta="right" c={value == null ? "dimmed" : undefined}>
          {formatBytes(value)}
        </Text>
        <Box
          w={56}
          h={6}
          style={{ flexShrink: 0, borderRadius: 3, background: "var(--argus-border)" }}
          aria-hidden
        >
          <Box h="100%" style={{ width: `${share * 100}%`, borderRadius: 3, background: color }} />
        </Box>
      </Group>
    </Table.Td>
  );
}

function useByteColors(): Record<ByteColumn, string> {
  const scheme = useComputedColorScheme("light");
  return {
    networkReceivedBytes: SERIES_COLORS[scheme][2],
    networkSentBytes: SERIES_COLORS[scheme][1],
    diskWriteBytes: DISK_COLORS[scheme].write,
    diskReadBytes: DISK_COLORS[scheme].read,
  };
}

const BYTE_COLUMNS: { key: ByteColumn; label: string; sort: UsageSortKey }[] = [
  { key: "networkReceivedBytes", label: "Downloaded", sort: "received" },
  { key: "networkSentBytes", label: "Uploaded", sort: "sent" },
  { key: "diskWriteBytes", label: "Disk written", sort: "diskWrite" },
  { key: "diskReadBytes", label: "Disk read", sort: "diskRead" },
];

function columnMax(rows: (UsageStats | null)[], key: ByteColumn): number {
  return Math.max(0, ...rows.map((stats) => stats?.[key] ?? 0));
}

/** The figures after the byte columns, shared by both tables. */
function StatCells({ stats }: { stats: UsageStats | null }) {
  return (
    <>
      <Table.Td>
        <UsageMeter value={stats?.cpuAveragePercent} label="Average CPU" width={48} />
      </Table.Td>
      <Table.Td>{formatPercent(stats?.cpuPeakPercent)}</Table.Td>
      <Table.Td>
        <UsageMeter value={stats?.memoryAveragePercent} label="Average memory" width={48} />
      </Table.Td>
      <Table.Td
        title={
          stats?.reportingSeconds != null
            ? `Reported for ${formatDuration(stats.reportingSeconds)}`
            : undefined
        }
      >
        {formatPercent(stats?.availabilityPercent, 1)}
      </Table.Td>
      <Table.Td>{formatTemperature(stats?.temperaturePeakCelsius, 0)}</Table.Td>
      <Table.Td>
        {stats == null ? (
          <Text span c="dimmed" fz="sm">
            –
          </Text>
        ) : (
          <>
            {stats.alerts}
            {stats.criticalAlerts > 0 && (
              <Text span c="crimson" fz="xs" ml={6}>
                {stats.criticalAlerts} critical
              </Text>
            )}
          </>
        )}
      </Table.Td>
    </>
  );
}

const STAT_HEADERS: { label: string; sort?: UsageSortKey; title?: string }[] = [
  { label: "Avg CPU", sort: "cpu" },
  { label: "Peak CPU" },
  { label: "Avg memory", sort: "memory" },
  { label: "Availability", sort: "availability", title: "Share of the time that the machine sent readings" },
  { label: "Hottest", sort: "temperature" },
  { label: "Alerts", sort: "alerts" },
];

interface FleetTableProps {
  summary: UsageSummary;
  option: PeriodOption;
  label: (index: number) => string;
  selected: number;
  onSelect: (index: number) => void;
}

/** One row per period with the whole fleet's figures; choosing a row shows its machines below. */
function FleetTable({ summary, option, label, selected, onSelect }: FleetTableProps) {
  const colors = useByteColors();
  const totals = summary.periods.map((period) => period.totals);
  const overall = combineStats(totals);

  return (
    <Table miw={1100} highlightOnHover>
      <Table.Thead>
        <Table.Tr>
          <Table.Th>{option.value === "Day" ? "Day" : option.value === "Month" ? "Month" : "Year"}</Table.Th>
          {BYTE_COLUMNS.map((column) => (
            <Table.Th key={column.key}>{column.label}</Table.Th>
          ))}
          {STAT_HEADERS.map((header) => (
            <Table.Th key={header.label} title={header.title}>
              {header.label}
            </Table.Th>
          ))}
        </Table.Tr>
      </Table.Thead>
      <Table.Tbody>
        {summary.periods.map((period, index) => (
          <Table.Tr
            key={period.start}
            onClick={() => onSelect(index)}
            aria-selected={index === selected}
            style={{
              cursor: "pointer",
              background: index === selected ? "var(--mantine-color-iris-light)" : undefined,
            }}
          >
            <Table.Td>
              <UnstyledButton
                onClick={() => onSelect(index)}
                fz="sm"
                fw={index === selected ? 600 : 400}
                title={periodDate(summary.period, period.start, summary.timeZone)}
              >
                {label(index)}
              </UnstyledButton>
            </Table.Td>
            {BYTE_COLUMNS.map((column) => (
              <ByteCell
                key={column.key}
                value={period.totals[column.key]}
                max={columnMax(totals, column.key)}
                color={colors[column.key]}
              />
            ))}
            <StatCells stats={period.hosts.length === 0 ? null : period.totals} />
          </Table.Tr>
        ))}
      </Table.Tbody>
      <Table.Tfoot>
        <Table.Tr style={{ fontWeight: 600 }}>
          <Table.Td>Over {option.span}</Table.Td>
          {BYTE_COLUMNS.map((column) => (
            <Table.Td key={column.key}>
              <Text fz="sm" fw={600} w={72} ta="right">
                {formatBytes(overall[column.key])}
              </Text>
            </Table.Td>
          ))}
          <StatCells stats={overall} />
        </Table.Tr>
      </Table.Tfoot>
    </Table>
  );
}

interface HostSectionProps {
  summary: UsageSummary;
  option: PeriodOption;
  label: (index: number) => string;
  selected: number;
  /** A period picked for this section alone (an index, or "all"); null follows `selected`. */
  choice: string | null;
  onChoose: (choice: string | null) => void;
}

const ALL = "all";

/** Each machine's figures for the chosen period, or for every period on show. */
function HostSection({ summary, option, label, selected, choice, onChoose }: HostSectionProps) {
  const colors = useByteColors();
  const [sort, setSort] = useState<{ key: UsageSortKey; descending: boolean }>({
    key: "received",
    descending: true,
  });

  const index =
    choice === ALL
      ? null
      : choice != null && Number(choice) < summary.periods.length
        ? Number(choice)
        : selected;
  const rows = sortHostUsage(hostUsage(summary, index), sort.key, sort.descending);
  const stats = rows.map((row) => row.stats);

  const toggle = (key: UsageSortKey) =>
    setSort((current) =>
      current.key === key ? { key, descending: !current.descending } : { key, descending: key !== "name" },
    );

  const options = [
    ...summary.periods.map((_, position) => ({ value: String(position), label: label(position) })),
    { value: ALL, label: `All of ${option.span}` },
  ];

  return (
    <Section
      title="By machine"
      mt="xl"
      action={
        <Select
          aria-label="Period for the machines"
          data={options}
          value={index == null ? ALL : String(index)}
          onChange={onChoose}
          allowDeselect={false}
          w={220}
          size="xs"
        />
      }
    >
      <Table miw={1100} highlightOnHover>
        <Table.Thead>
          <Table.Tr>
            <SortHeader sortKey="name" sort={sort} onSort={toggle}>
              Machine
            </SortHeader>
            {BYTE_COLUMNS.map((column) => (
              <SortHeader key={column.key} sortKey={column.sort} sort={sort} onSort={toggle}>
                {column.label}
              </SortHeader>
            ))}
            {STAT_HEADERS.map((header) =>
              header.sort ? (
                <SortHeader
                  key={header.label}
                  sortKey={header.sort}
                  sort={sort}
                  onSort={toggle}
                  title={header.title}
                >
                  {header.label}
                </SortHeader>
              ) : (
                <Table.Th key={header.label}>{header.label}</Table.Th>
              ),
            )}
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {rows.map((row) => (
            <Table.Tr key={row.hostId}>
              <Table.Td>
                <Anchor component={Link} to={`/hosts/${row.hostId}`} fz="sm" fw={500}>
                  {row.displayName}
                </Anchor>
              </Table.Td>
              {BYTE_COLUMNS.map((column) => (
                <ByteCell
                  key={column.key}
                  value={row.stats?.[column.key] ?? null}
                  max={columnMax(stats, column.key)}
                  color={colors[column.key]}
                />
              ))}
              <StatCells stats={row.stats} />
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </Section>
  );
}

interface SortHeaderProps {
  sortKey: UsageSortKey;
  sort: { key: UsageSortKey; descending: boolean };
  onSort: (key: UsageSortKey) => void;
  title?: string;
  children: ReactNode;
}

function SortHeader({ sortKey, sort, onSort, title, children }: SortHeaderProps) {
  const active = sort.key === sortKey;
  const Icon = active && sort.descending ? IconChevronDown : IconChevronUp;
  return (
    <Table.Th aria-sort={active ? (sort.descending ? "descending" : "ascending") : "none"} title={title}>
      <UnstyledButton onClick={() => onSort(sortKey)} fw={600} fz="sm">
        <Group gap={4} wrap="nowrap">
          {children}
          <Icon size={14} style={{ opacity: active ? 1 : 0.25 }} aria-hidden />
        </Group>
      </UnstyledButton>
    </Table.Th>
  );
}
