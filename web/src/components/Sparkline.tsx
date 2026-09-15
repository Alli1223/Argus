import { Text } from "@mantine/core";
import { layoutSparkline } from "../lib/sparkline";

interface SparklineProps {
  values: (number | null)[];
  color: string;
  /** What the line shows, for screen readers ("/ used, from 41% to 43%"). */
  label: string;
  width?: number;
  height?: number;
}

/** A trend in a table cell: a 2px line with a dot on the newest reading, no axes. */
export function Sparkline({ values, color, label, width = 96, height = 28 }: SparklineProps) {
  const { path, end } = layoutSparkline(values, width, height);
  if (!end) {
    return (
      <Text fz="sm" c="dimmed">
        –
      </Text>
    );
  }

  return (
    <svg width={width} height={height} viewBox={`0 0 ${width} ${height}`} role="img" aria-label={label}>
      <path
        d={path}
        fill="none"
        stroke={color}
        strokeWidth={2}
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <circle cx={end.x} cy={end.y} r={4} fill={color} stroke="var(--argus-surface)" strokeWidth={2} />
    </svg>
  );
}
