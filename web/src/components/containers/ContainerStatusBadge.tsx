import { Box, Group, Text } from "@mantine/core";
import type { ContainerSummary } from "../../api/types";
import { containerStatus, type ContainerTone } from "../../lib/containers";

const DOT: Record<ContainerTone, string> = {
  healthy: "var(--mantine-color-healthy-filled)",
  warning: "var(--mantine-color-bronze-filled)",
  critical: "var(--mantine-color-crimson-filled)",
  muted: "var(--mantine-color-gray-5)",
};

/** A container's state as a dot and a word (never colour alone). */
export function ContainerStatusBadge({ container }: { container: ContainerSummary }) {
  const status = containerStatus(container);
  return (
    <Group gap={6} wrap="nowrap">
      <Box
        w={8}
        h={8}
        aria-hidden
        style={{ flexShrink: 0, borderRadius: "50%", background: DOT[status.tone] }}
      />
      <Text fz="sm" fw={status.problem ? 600 : undefined}>
        {status.label}
      </Text>
    </Group>
  );
}
