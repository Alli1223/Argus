import { Group, Text, Tooltip } from "@mantine/core";
import type { LiveState } from "../api/live";

const STATES: Record<LiveState, { label: string; color: string; hint: string }> = {
  connecting: { label: "Connecting", color: "gray", hint: "Connecting to live updates." },
  live: { label: "Live", color: "healthy", hint: "New readings and alerts appear as they arrive." },
  reconnecting: {
    label: "Reconnecting",
    color: "bronze",
    hint: "The live connection dropped and Argus is trying again. Pages still refresh every 30 seconds.",
  },
  offline: {
    label: "Not live",
    color: "gray",
    hint: "No live connection. Pages still refresh every 30 seconds.",
  },
};

/** Whether the page hears about new readings as they arrive. */
export function LiveIndicator({ state }: { state: LiveState }) {
  const { label, color, hint } = STATES[state];
  return (
    <Tooltip label={hint} multiline w={260}>
      <Group gap={6} wrap="nowrap" visibleFrom="xs" tabIndex={0} role="status">
        <span
          style={{ width: 8, height: 8, borderRadius: 4, background: `var(--mantine-color-${color}-filled)` }}
          aria-hidden
        />
        <Text fz="xs" c="dimmed">
          {label}
        </Text>
      </Group>
    </Tooltip>
  );
}
