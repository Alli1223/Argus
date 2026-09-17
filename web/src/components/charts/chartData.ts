import { formatBytes, formatPercent, formatRate, formatTemperature } from "../../lib/format";

/** How a chart's values read: percentages, bytes, byte rates, load averages or temperatures. */
export type ChartUnit = "percent" | "bytes" | "bytesPerSecond" | "load" | "celsius";

/** One line on a chart. Values line up with the chart's timestamps; null is a gap. */
export interface ChartSeries {
  key: string;
  label: string;
  color: string;
  values: (number | null)[];
}

const MISSING = "–";
const BYTE_STEPS = [1, 2, 5, 10, 20, 50, 100, 200, 500];

/** Tick steps in binary multiples, so byte axes read "500 KB/s, 1 MB/s" instead of "976.6 KB/s". */
export const BYTE_INCREMENTS = [0, 1, 2, 3, 4].flatMap((power) =>
  BYTE_STEPS.map((step) => step * 1024 ** power),
);

/** A value in a tooltip, legend or table: "42.5%", "1.2 MB/s", "0.73", "61.5 °C". */
export function formatValue(value: number | null | undefined, unit: ChartUnit): string {
  if (value == null || !Number.isFinite(value)) return MISSING;
  if (unit === "percent") return formatPercent(value, 1);
  if (unit === "bytes") return formatBytes(value);
  if (unit === "bytesPerSecond") return formatRate(value);
  if (unit === "celsius") return formatTemperature(value);
  return value.toFixed(2);
}

/** An axis tick, without needless decimals: "50%", "1 MB/s", "1.5", "60 °C". */
export function formatTick(value: number, unit: ChartUnit): string {
  // Small percentages, such as an idle container's CPU, keep their decimals so the ticks differ.
  if (unit === "percent") return `${Number(value.toFixed(2))}%`;
  if (unit === "bytes") return formatBytes(value).replace(/\.0 /, " ");
  if (unit === "bytesPerSecond") return `${formatBytes(value).replace(/\.0 /, " ")}/s`;
  if (unit === "celsius") return `${Number(value.toFixed(1))} °C`;
  return String(Number(value.toFixed(2)));
}

/**
 * The y-axis of a temperature chart: whole tens around the readings, at least 20 °C tall. Zero means
 * nothing on this scale, so the axis does not start there; the minimum height keeps a wobble of a
 * degree from looking like a swing.
 */
export function temperatureAxis(min: number | null, max: number | null): [number, number] {
  if (min == null || max == null || !Number.isFinite(min) || !Number.isFinite(max)) return [0, 100];
  const low = Math.floor((min - 5) / 10) * 10;
  return [low, Math.max(Math.ceil((max + 5) / 10) * 10, low + 20)];
}

const clock = new Intl.DateTimeFormat(undefined, { hour: "2-digit", minute: "2-digit" });
const day = new Intl.DateTimeFormat(undefined, { day: "numeric", month: "short" });
const moment = new Intl.DateTimeFormat(undefined, {
  weekday: "short",
  day: "numeric",
  month: "short",
  hour: "2-digit",
  minute: "2-digit",
});

/** X-axis labels: clock times within a day; dates at midnight and when ticks are a day or more apart. */
export function formatAxisTime(seconds: number, stepSeconds: number): string {
  const date = new Date(seconds * 1000);
  const midnight = date.getHours() === 0 && date.getMinutes() === 0;
  return stepSeconds >= 86_400 || midnight ? day.format(date) : clock.format(date);
}

/** One moment, for tooltips and table rows: "Tue 15 Sep, 14:32". */
export function formatPointTime(seconds: number): string {
  return moment.format(new Date(seconds * 1000));
}

/** A zoomed window: "Tue 15 Sep, 10:12 to 11:40". */
export function formatWindow(from: number, to: number): string {
  const start = new Date(from * 1000);
  const end = new Date(to * 1000);
  const sameDay = start.toDateString() === end.toDateString();
  return `${moment.format(start)} to ${sameDay ? clock.format(end) : moment.format(end)}`;
}
