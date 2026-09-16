import {
  Alert,
  Anchor,
  Badge,
  Box,
  Button,
  Group,
  SegmentedControl,
  Select,
  Skeleton,
  Table,
  Text,
  TextInput,
  UnstyledButton,
} from "@mantine/core";
import { notifications } from "@mantine/notifications";
import {
  IconArrowDown,
  IconArrowUp,
  IconArrowUpCircle,
  IconChevronDown,
  IconChevronUp,
  IconPlus,
  IconSearch,
} from "@tabler/icons-react";
import { useMemo, useState, type ReactNode } from "react";
import { Link } from "react-router";
import { useHosts } from "../../api/hosts";
import { useRequestAllAgentUpdates } from "../../api/updates";
import type { HostSummary } from "../../api/types";
import { HostStatusBadge, PlatformIcon } from "../../components/HostBits";
import { PageHeader } from "../../components/PageHeader";
import { ErrorScreen, MessageScreen } from "../../components/Screens";
import { UsageMeter } from "../../components/UsageMeter";
import { formatAgo, formatDuration, formatRate } from "../../lib/format";
import { agentUpdateState, outdatedAgents } from "../../lib/updates";
import { useNow } from "../../lib/useNow";

type SortKey = "name" | "status" | "cpu" | "memory" | "disk" | "lastSeen";
type StatusFilter = "all" | "Online" | "Offline";

const sortValue: Record<SortKey, (host: HostSummary) => string | number | null> = {
  name: (host) => host.displayName.toLowerCase(),
  status: (host) => (host.status === "Online" ? 1 : 0),
  cpu: (host) => host.latest?.cpuPercent ?? null,
  memory: (host) => host.latest?.memoryPercent ?? null,
  disk: (host) => host.latest?.diskUsedPercent ?? null,
  lastSeen: (host) => (host.lastSeenAt ? Date.parse(host.lastSeenAt) : null),
};

function matches(host: HostSummary, search: string) {
  if (!search) return true;
  const needle = search.toLowerCase();
  return [host.displayName, host.hostname, host.osName ?? "", ...host.tags].some((text) =>
    text.toLowerCase().includes(needle),
  );
}

export function HostsPage() {
  const hosts = useHosts();
  const updateAll = useRequestAllAgentUpdates();
  const now = useNow();
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState<StatusFilter>("all");
  const [tag, setTag] = useState<string | null>(null);
  const [sort, setSort] = useState<{ key: SortKey; descending: boolean }>({ key: "name", descending: false });

  const all = useMemo(() => hosts.data ?? [], [hosts.data]);
  const tags = useMemo(() => [...new Set(all.flatMap((host) => host.tags))].sort(), [all]);
  const rows = useMemo(() => {
    const value = sortValue[sort.key];
    return all
      .filter((host) => status === "all" || host.status === status)
      .filter((host) => !tag || host.tags.includes(tag))
      .filter((host) => matches(host, search.trim()))
      .sort((a, b) => {
        const x = value(a);
        const y = value(b);
        if (x === y) return a.displayName.localeCompare(b.displayName);
        if (x === null) return 1; // missing readings always sort last
        if (y === null) return -1;
        const order = x < y ? -1 : 1;
        return sort.descending ? -order : order;
      });
  }, [all, status, tag, search, sort]);

  if (hosts.isError) return <ErrorScreen error={hosts.error} onRetry={() => void hosts.refetch()} />;

  const online = all.filter((host) => host.status === "Online").length;
  const outdated = outdatedAgents(all);
  const addSystem = (
    <Button component={Link} to="/systems" leftSection={<IconPlus size={16} />}>
      Add a system
    </Button>
  );

  if (hosts.isSuccess && all.length === 0) {
    return (
      <MessageScreen title="No systems yet" action={addSystem}>
        Install the Argus agent on a machine and it shows up here within a minute.
      </MessageScreen>
    );
  }

  const filtered = search !== "" || status !== "all" || tag !== null;
  const toggleSort = (key: SortKey) =>
    setSort((current) =>
      current.key === key
        ? { key, descending: !current.descending }
        : { key, descending: key !== "name" && key !== "status" },
    );

  return (
    <>
      <PageHeader
        title="Hosts"
        description={hosts.isSuccess ? `${online} of ${all.length} online` : undefined}
        actions={addSystem}
      />

      {outdated.count > 0 && (
        <Alert
          color="iris"
          mb="md"
          icon={<IconArrowUpCircle size={18} />}
          title={`${outdated.count === 1 ? "An agent" : `${outdated.count} agents`} can update to ${outdated.version}`}
        >
          <Group justify="space-between" gap="sm" wrap="wrap">
            <Text fz="sm">
              Each agent installs the new version after its next report, then restarts itself.
            </Text>
            <Button
              size="compact-sm"
              loading={updateAll.isPending}
              onClick={() =>
                updateAll.mutate(undefined, {
                  onError: (error) =>
                    notifications.show({
                      color: "crimson",
                      title: "The agents were not asked to update",
                      message: error.message,
                    }),
                })
              }
            >
              Update all
            </Button>
          </Group>
        </Alert>
      )}

      <Group gap="sm" mb="md" wrap="wrap">
        <TextInput
          aria-label="Search hosts"
          placeholder="Search names, systems and tags"
          leftSection={<IconSearch size={16} />}
          value={search}
          onChange={(event) => setSearch(event.currentTarget.value)}
          w={280}
        />
        <SegmentedControl
          aria-label="Filter by status"
          value={status}
          onChange={(value) => setStatus(value as StatusFilter)}
          data={[
            { label: "All", value: "all" },
            { label: "Online", value: "Online" },
            { label: "Offline", value: "Offline" },
          ]}
        />
        {tags.length > 0 && (
          <Select
            aria-label="Filter by tag"
            placeholder="Any tag"
            data={tags}
            value={tag}
            onChange={setTag}
            clearable
            w={180}
          />
        )}
      </Group>

      <Box className="argus-surface" style={{ borderRadius: "var(--mantine-radius-sm)", overflowX: "auto" }}>
        <Table miw={960}>
          <Table.Thead>
            <Table.Tr>
              <SortableHeader sortKey="name" sort={sort} onSort={toggleSort}>
                Host
              </SortableHeader>
              <SortableHeader sortKey="status" sort={sort} onSort={toggleSort}>
                Status
              </SortableHeader>
              <SortableHeader sortKey="cpu" sort={sort} onSort={toggleSort}>
                CPU
              </SortableHeader>
              <SortableHeader sortKey="memory" sort={sort} onSort={toggleSort}>
                Memory
              </SortableHeader>
              <SortableHeader sortKey="disk" sort={sort} onSort={toggleSort}>
                Fullest disk
              </SortableHeader>
              <Table.Th>Network</Table.Th>
              <Table.Th>Uptime</Table.Th>
              <SortableHeader sortKey="lastSeen" sort={sort} onSort={toggleSort}>
                Last report
              </SortableHeader>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {hosts.isPending &&
              Array.from({ length: 4 }, (_, index) => (
                <Table.Tr key={index}>
                  <Table.Td colSpan={8}>
                    <Skeleton height={28} />
                  </Table.Td>
                </Table.Tr>
              ))}
            {rows.map((host) => (
              <HostRow key={host.id} host={host} now={now} />
            ))}
            {hosts.isSuccess && rows.length === 0 && filtered && (
              <Table.Tr>
                <Table.Td colSpan={8}>
                  <Group gap="sm" py="md" justify="center">
                    <Text c="dimmed">No hosts match these filters.</Text>
                    <Button
                      variant="subtle"
                      size="compact-sm"
                      onClick={() => {
                        setSearch("");
                        setStatus("all");
                        setTag(null);
                      }}
                    >
                      Clear filters
                    </Button>
                  </Group>
                </Table.Td>
              </Table.Tr>
            )}
          </Table.Tbody>
        </Table>
      </Box>
    </>
  );
}

function HostRow({ host, now }: { host: HostSummary; now: number }) {
  const latest = host.latest;
  // An offline host's last readings are history, not the present: show them, but quietly.
  const stale = host.status === "Offline";

  return (
    <Table.Tr style={stale ? { opacity: 0.62 } : undefined}>
      <Table.Td miw={240}>
        <Group gap={10} wrap="nowrap">
          <PlatformIcon platform={host.platform} />
          <div style={{ minWidth: 0 }}>
            <Anchor component={Link} to={`/hosts/${host.id}`} fw={600} fz="sm">
              {host.displayName}
            </Anchor>
            {/* Tags wrap under the system name rather than being squeezed into ellipses. */}
            <Group gap={6} wrap="wrap" style={{ rowGap: 2 }}>
              <Text fz="xs" c="dimmed" truncate="end">
                {host.hostname !== host.displayName ? host.hostname : (host.osName ?? host.platform)}
              </Text>
              {host.tags.map((tag) => (
                <Badge key={tag} size="xs" variant="outline" color="gray">
                  {tag}
                </Badge>
              ))}
              <AgentUpdateBadge host={host} now={now} />
            </Group>
          </div>
        </Group>
      </Table.Td>
      <Table.Td>
        <HostStatusBadge status={host.status} />
      </Table.Td>
      <Table.Td>
        <UsageMeter value={latest?.cpuPercent} label={`${host.displayName} CPU`} />
      </Table.Td>
      <Table.Td>
        <UsageMeter value={latest?.memoryPercent} label={`${host.displayName} memory`} />
      </Table.Td>
      <Table.Td>
        <UsageMeter value={latest?.diskUsedPercent} label={`${host.displayName} fullest disk`} />
      </Table.Td>
      <Table.Td>
        <Group gap={10} wrap="nowrap" className="argus-data" fz="sm">
          <Group gap={2} wrap="nowrap" aria-label="Received">
            <IconArrowDown size={13} aria-hidden />
            {formatRate(latest?.netRxBytesPerSec)}
          </Group>
          <Group gap={2} wrap="nowrap" aria-label="Sent">
            <IconArrowUp size={13} aria-hidden />
            {formatRate(latest?.netTxBytesPerSec)}
          </Group>
        </Group>
      </Table.Td>
      <Table.Td className="argus-data">{formatDuration(latest?.uptimeSeconds)}</Table.Td>
      <Table.Td className="argus-data">{formatAgo(host.lastSeenAt, now)}</Table.Td>
    </Table.Tr>
  );
}

/** A hint that the host's agent can, or is about to, update; the host page has the details. */
function AgentUpdateBadge({ host, now }: { host: HostSummary; now: number }) {
  const state = agentUpdateState(host.agentUpdate, now);
  if (state.kind === "none") return null;
  const [label, color] =
    state.kind === "available"
      ? [`Agent ${state.version} available`, "iris"]
      : state.kind === "updating"
        ? [`Updating agent to ${state.version}`, "iris"]
        : ["Agent update failed", "crimson"];
  return (
    <Badge size="xs" variant="light" color={color}>
      {label}
    </Badge>
  );
}

interface SortableHeaderProps {
  sortKey: SortKey;
  sort: { key: SortKey; descending: boolean };
  onSort: (key: SortKey) => void;
  children: ReactNode;
}

function SortableHeader({ sortKey, sort, onSort, children }: SortableHeaderProps) {
  const active = sort.key === sortKey;
  const Icon = active && sort.descending ? IconChevronDown : IconChevronUp;
  return (
    <Table.Th aria-sort={active ? (sort.descending ? "descending" : "ascending") : "none"}>
      <UnstyledButton onClick={() => onSort(sortKey)} fw={600} fz="sm">
        <Group gap={4} wrap="nowrap">
          {children}
          <Icon size={14} style={{ opacity: active ? 1 : 0.25 }} aria-hidden />
        </Group>
      </UnstyledButton>
    </Table.Th>
  );
}
