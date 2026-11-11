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
import type { UsageStats, UsageSummary } from "../../api/types";
import { DISK_COLORS, SERIES_COLORS } from "../../components/charts/chartPalette";
import { PageHeader } from "../../components/PageHeader";
import { ErrorScreen, MessageScreen } from "../../components/Screens";
import { Section } from "../../components/Section";
import { UsageMeter } from "../../components/UsageMeter";
import { formatBytes, formatDuration, formatPercent, formatTemperature } from "../../lib/format";
import {
  combineStats,
  hostUsage,
  periodDate,
  periodLabel,
  periodOption,
  PERIOD_OPTIONS,
  sortHostUsage,
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
}

/** The selected period's fleet totals, each with the period before beside it. */
function Headline({ stats, previous, previousLabel }: HeadlineProps) {
  const before = (text: string) => (previous ? `${previousLabel}: ${text}` : undefined);

  return (
    <SimpleGrid cols={{ base: 2, sm: 3, lg: 6 }} spacing="md">
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
