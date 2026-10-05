import { Text } from "@mantine/core";
import type { ReactNode } from "react";
import classes from "./Gauge.module.css";

/** The dial runs clockwise from lower left to lower right, leaving the bottom open. */
const START = 150;
const SWEEP = 240;
const CENTRE = { x: 60, y: 58 };
const RADIUS = 46;
const STROKE = 9;

function point(degrees: number) {
  const radians = (degrees * Math.PI) / 180;
  return { x: CENTRE.x + RADIUS * Math.cos(radians), y: CENTRE.y + RADIUS * Math.sin(radians) };
}

const from = point(START);
const to = point(START + SWEEP);
const ARC = `M ${from.x} ${from.y} A ${RADIUS} ${RADIUS} 0 1 1 ${to.x} ${to.y}`;

export interface GaugeProps {
  /** What is measured, above the dial. */
  label: string;
  /** How full the dial is, 0 to 1; null when there is no reading. */
  fraction: number | null;
  /** The reading as a number, such as "22.3", and its unit, such as "%" or "MB/s". */
  value: string;
  unit: string;
  /** The value colour, and its faint track. */
  color: string;
  trackColor: string;
  /** What the two ends of the dial stand for, such as "0" and "100%". */
  min: string;
  max: string;
  /** A line under the dial, such as how many systems it covers. */
  caption?: ReactNode;
}

/** One reading as a dial: the number in the middle, the arc filled to it. */
export function Gauge({ label, fraction, value, unit, color, trackColor, min, max, caption }: GaugeProps) {
  const filled = fraction == null ? 0 : Math.min(1, Math.max(0, fraction));
  return (
    <figure className={classes.gauge} style={{ margin: 0 }}>
      <Text component="figcaption" fz="sm" fw={600} ta="center">
        {label}
      </Text>
      <svg
        viewBox="0 0 120 100"
        className={classes.dial}
        role="img"
        aria-label={`${label}: ${value} ${unit}`}
      >
        <path d={ARC} fill="none" stroke={trackColor} strokeWidth={STROKE} strokeLinecap="round" />
        {filled > 0 && (
          <path
            className={classes.value}
            d={ARC}
            fill="none"
            stroke={color}
            strokeWidth={STROKE}
            strokeLinecap="round"
            pathLength={100}
            strokeDasharray={100}
            strokeDashoffset={100 - filled * 100}
          />
        )}
        <text x={CENTRE.x} y={CENTRE.y + 4} textAnchor="middle" className={classes.number}>
          {value}
        </text>
        <text x={CENTRE.x} y={CENTRE.y + 18} textAnchor="middle" className={classes.unit}>
          {unit}
        </text>
        {/* The end labels grow inwards from the dial's ends, so long ones stay inside it. */}
        <text x={from.x - 4} y={from.y + 16} textAnchor="start" className={classes.end}>
          {min}
        </text>
        <text x={to.x + 4} y={to.y + 16} textAnchor="end" className={classes.end}>
          {max}
        </text>
      </svg>
      {caption && (
        <Text fz="xs" c="dimmed" ta="center" className="argus-data">
          {caption}
        </Text>
      )}
    </figure>
  );
}
