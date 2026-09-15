import { Box, Group, Text } from "@mantine/core";
import { formatPercent } from "../lib/format";
import { severityColor, severityOf } from "../lib/severity";

interface UsageMeterProps {
  value: number | null | undefined;
  /** What is being measured, for screen readers ("web-1 CPU"). */
  label: string;
  width?: number;
}

/**
 * A utilisation meter: the fill's colour carries severity and its track is a lighter step of the
 * same hue, so the state reads across the whole bar. The number is always shown beside it.
 */
export function UsageMeter({ value, label, width = 64 }: UsageMeterProps) {
  if (value == null) {
    return (
      <Text c="dimmed" fz="sm">
        –
      </Text>
    );
  }

  const color = severityColor[severityOf(value)];
  const clamped = Math.min(100, Math.max(0, value));
  return (
    <Group gap={8} wrap="nowrap">
      <Box
        role="meter"
        aria-label={label}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={Math.round(value)}
        w={width}
        h={6}
        style={{
          flexShrink: 0,
          borderRadius: 3,
          overflow: "hidden",
          background: `var(--mantine-color-${color}-light)`,
        }}
      >
        <Box
          h="100%"
          style={{
            width: `${clamped}%`,
            borderRadius: 3,
            background: `var(--mantine-color-${color}-filled)`,
          }}
        />
      </Box>
      <Text className="argus-data" fz="sm" miw={34} ta="right">
        {formatPercent(value)}
      </Text>
    </Group>
  );
}
