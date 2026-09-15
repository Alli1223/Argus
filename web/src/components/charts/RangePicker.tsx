import { Button, Group, SegmentedControl, Text, VisuallyHidden } from "@mantine/core";
import type { ReactNode } from "react";
import { DEFAULT_RANGE, RANGE_PRESETS, type RangePreset, type TimeRange } from "../../lib/timeRange";
import { formatWindow } from "./chartData";

interface RangePickerProps {
  range: TimeRange;
  onChange: (range: TimeRange) => void;
  /** A note at the end of the row, such as how wide each point is. */
  children?: ReactNode;
}

/** The one filter row above a page's charts: preset windows first, then any window zoomed into. */
export function RangePicker({ range, onChange, children }: RangePickerProps) {
  return (
    <Group gap="sm" wrap="wrap" align="center">
      <SegmentedControl
        aria-label="Time range"
        size="xs"
        value={"preset" in range ? range.preset : ""}
        onChange={(value) => onChange({ preset: value as RangePreset })}
        data={RANGE_PRESETS.map(({ value, label, name }) => ({
          value,
          label: (
            <>
              <span aria-hidden>{label}</span>
              <VisuallyHidden>{name}</VisuallyHidden>
            </>
          ),
        }))}
      />
      {!("preset" in range) && (
        <Group gap={6} wrap="nowrap">
          <Text fz="sm" className="argus-data">
            {formatWindow(range.from, range.to)}
          </Text>
          <Button variant="subtle" size="compact-sm" onClick={() => onChange(DEFAULT_RANGE)}>
            Reset zoom
          </Button>
        </Group>
      )}
      {children}
    </Group>
  );
}
