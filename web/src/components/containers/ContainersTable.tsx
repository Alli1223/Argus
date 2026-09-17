import { Anchor, Box, Group, Table, Text, VisuallyHidden } from "@mantine/core";
import { IconArrowDown, IconArrowUp } from "@tabler/icons-react";
import { Link } from "react-router";
import type { ContainerSummary } from "../../api/types";
import { composeName, containerPath, containerStatus } from "../../lib/containers";
import { formatBytes, formatDuration, formatRate } from "../../lib/format";
import { UsageMeter } from "../UsageMeter";
import { ContainerStatusBadge } from "./ContainerStatusBadge";

export interface ContainerRow extends ContainerSummary {
  hostId: string;
  hostName?: string;
}

interface ContainersTableProps {
  rows: ContainerRow[];
  now: number;
  /** Adds a column naming each container's host, for lists that span hosts. */
  showHost?: boolean;
}

/** Containers with their state, how long they have been in it, and what they use. */
export function ContainersTable({ rows, now, showHost = false }: ContainersTableProps) {
  return (
    <Table miw={showHost ? 1040 : 900}>
      <Table.Thead>
        <Table.Tr>
          <Table.Th>Container</Table.Th>
          {showHost && <Table.Th>Host</Table.Th>}
          <Table.Th>State</Table.Th>
          <Table.Th>CPU</Table.Th>
          <Table.Th>Memory</Table.Th>
          <Table.Th>Network</Table.Th>
          <Table.Th ta="right">Restarts</Table.Th>
        </Table.Tr>
      </Table.Thead>
      <Table.Tbody>
        {rows.map((row) => {
          const status = containerStatus(row);
          const usage = row.usage;
          return (
            <Table.Tr key={`${row.hostId}/${row.name}`}>
              <Table.Td>
                <Box maw={320}>
                  <Anchor component={Link} to={containerPath(row.hostId, row.name)} fz="sm" fw={600}>
                    {row.name}
                  </Anchor>
                  <Text fz="xs" c="dimmed" truncate="end" title={row.image}>
                    {composeName(row) ?? row.image}
                  </Text>
                </Box>
              </Table.Td>
              {showHost && (
                <Table.Td>
                  <Anchor component={Link} to={`/hosts/${row.hostId}`} fz="sm">
                    {row.hostName}
                  </Anchor>
                </Table.Td>
              )}
              <Table.Td>
                <ContainerStatusBadge container={row} />
                <Text fz="xs" c={status.reason ? "crimson" : "dimmed"}>
                  {status.reason ?? `For ${formatDuration((now - Date.parse(row.stateSince)) / 1000)}`}
                </Text>
              </Table.Td>
              <Table.Td>
                {usage ? <UsageMeter value={usage.cpuPercent} label={`${row.name} CPU`} /> : <Missing />}
              </Table.Td>
              <Table.Td>
                {usage ? (
                  <Text fz="sm">
                    {formatBytes(usage.memoryBytes)}
                    {usage.memoryLimitBytes != null && (
                      <Text span fz="xs" c="dimmed">
                        {" "}
                        of {formatBytes(usage.memoryLimitBytes)}
                      </Text>
                    )}
                  </Text>
                ) : (
                  <Missing />
                )}
              </Table.Td>
              <Table.Td>
                {usage?.netRxBytesPerSec != null ? (
                  <Group gap={12} wrap="nowrap" className="argus-data">
                    <Group gap={2} wrap="nowrap">
                      <IconArrowDown size={13} aria-hidden />
                      <VisuallyHidden>Received</VisuallyHidden>
                      {formatRate(usage.netRxBytesPerSec)}
                    </Group>
                    <Group gap={2} wrap="nowrap">
                      <IconArrowUp size={13} aria-hidden />
                      <VisuallyHidden>Sent</VisuallyHidden>
                      {formatRate(usage.netTxBytesPerSec)}
                    </Group>
                  </Group>
                ) : (
                  <Missing title={usage ? "Shares the host's network" : undefined} />
                )}
              </Table.Td>
              <Table.Td
                ta="right"
                title={row.restartsLastHour > 0 ? `${row.restartsLastHour} in the last hour` : undefined}
              >
                {row.restartCount}
              </Table.Td>
            </Table.Tr>
          );
        })}
      </Table.Tbody>
    </Table>
  );
}

function Missing({ title }: { title?: string }) {
  return (
    <Text fz="sm" c="dimmed" title={title}>
      –
    </Text>
  );
}
