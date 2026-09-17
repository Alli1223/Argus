import {
  Alert,
  Anchor,
  Box,
  Code,
  Group,
  SegmentedControl,
  Skeleton,
  Stack,
  Text,
  TextInput,
} from "@mantine/core";
import { IconAlertTriangle, IconSearch } from "@tabler/icons-react";
import { useMemo, useState } from "react";
import { Link } from "react-router";
import { useContainerFleet } from "../../api/containers";
import { ContainersTable, type ContainerRow } from "../../components/containers/ContainersTable";
import { PageHeader } from "../../components/PageHeader";
import { ErrorScreen, MessageScreen } from "../../components/Screens";
import {
  composeName,
  containerStatus,
  countContainers,
  describeCounts,
  isUp,
  sortContainers,
} from "../../lib/containers";
import { useNow } from "../../lib/useNow";

type Filter = "problems" | "running" | "stopped" | "all";

function matches(row: ContainerRow, filter: Filter, search: string) {
  if (filter === "problems" && !containerStatus(row).problem) return false;
  if (filter === "running" && !isUp(row)) return false;
  if (filter === "stopped" && isUp(row)) return false;
  if (!search) return true;
  const needle = search.toLowerCase();
  return [row.name, row.image, row.hostName ?? "", composeName(row) ?? ""].some((text) =>
    text.toLowerCase().includes(needle),
  );
}

/** Every container on every host that reports them, problems first. */
export function ContainersPage() {
  const fleet = useContainerFleet();
  const now = useNow();
  const [filter, setFilter] = useState<Filter>("all");
  const [search, setSearch] = useState("");

  const rows = useMemo(
    () =>
      sortContainers(
        (fleet.data ?? []).flatMap((host) =>
          host.containers.map((container) => ({
            ...container,
            hostId: host.hostId,
            hostName: host.hostName,
          })),
        ),
      ),
    [fleet.data],
  );
  const shown = rows.filter((row) => matches(row, filter, search.trim()));
  const counts = countContainers(rows);
  const blind = (fleet.data ?? []).filter((host) => host.problem);

  if (fleet.isError && !fleet.data)
    return <ErrorScreen error={fleet.error} onRetry={() => void fleet.refetch()} />;

  if (fleet.isSuccess && fleet.data.length === 0) {
    return (
      <MessageScreen title="No containers yet">
        <Stack gap="xs">
          <span>
            Agents list Docker's containers on Linux machines once they may use Docker. Install the agent with
            the <Code>--docker</Code> option, or run its install command again with it.
          </span>
        </Stack>
      </MessageScreen>
    );
  }

  return (
    <>
      <PageHeader
        title="Containers"
        description={
          fleet.data
            ? `${describeCounts(counts)}, on ${fleet.data.length} ${fleet.data.length === 1 ? "host" : "hosts"}.`
            : undefined
        }
      />

      {blind.map((host) => (
        <Alert
          key={host.hostId}
          color="bronze"
          icon={<IconAlertTriangle size={18} />}
          mb="md"
          title={
            <>
              <Anchor component={Link} to={`/hosts/${host.hostId}`} inherit>
                {host.hostName}
              </Anchor>
              's agent cannot read Docker
            </>
          }
        >
          {host.problem}
        </Alert>
      ))}

      <Group gap="sm" mb="md" wrap="wrap">
        <TextInput
          aria-label="Search containers"
          placeholder="Search names, images and hosts"
          leftSection={<IconSearch size={16} />}
          value={search}
          onChange={(event) => setSearch(event.currentTarget.value)}
          w={280}
        />
        <SegmentedControl
          aria-label="Show"
          value={filter}
          onChange={(value) => setFilter(value as Filter)}
          data={[
            { label: "All", value: "all" },
            {
              label: `Need attention${counts.problems > 0 ? ` (${counts.problems})` : ""}`,
              value: "problems",
            },
            { label: "Running", value: "running" },
            { label: "Stopped", value: "stopped" },
          ]}
        />
      </Group>

      <Box className="argus-surface" style={{ borderRadius: "var(--mantine-radius-sm)", overflowX: "auto" }}>
        {fleet.isPending ? (
          <Skeleton h={160} />
        ) : shown.length === 0 ? (
          <Text fz="sm" c="dimmed" p="md">
            {filter === "problems" && !search ? "Every container is fine." : "No containers match."}
          </Text>
        ) : (
          <ContainersTable rows={shown} now={now} showHost />
        )}
      </Box>
    </>
  );
}
