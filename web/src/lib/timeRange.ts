/** Chart time ranges: a preset window ending now, or a fixed window someone zoomed into. */
export type RangePreset = "1h" | "6h" | "24h" | "7d" | "30d";

export type TimeRange = { preset: RangePreset } | { from: number; to: number };

export const RANGE_PRESETS: readonly { value: RangePreset; label: string; name: string; seconds: number }[] =
  [
    { value: "1h", label: "1h", name: "Last hour", seconds: 3_600 },
    { value: "6h", label: "6h", name: "Last 6 hours", seconds: 6 * 3_600 },
    { value: "24h", label: "24h", name: "Last 24 hours", seconds: 24 * 3_600 },
    { value: "7d", label: "7d", name: "Last 7 days", seconds: 7 * 86_400 },
    { value: "30d", label: "30d", name: "Last 30 days", seconds: 30 * 86_400 },
  ];

const DEFAULT_PRESET: RangePreset = "6h";
export const DEFAULT_RANGE: TimeRange = { preset: DEFAULT_PRESET };

/** Narrower windows than this are a stray click, not a zoom. */
const MIN_WINDOW_SECONDS = 60;

function presetOf(value: string | null) {
  return RANGE_PRESETS.find((preset) => preset.value === value);
}

/** Reads `?range=24h` or `?from=…&to=…` (ISO times); anything else gives the default range. */
export function rangeFromParams(params: URLSearchParams): TimeRange {
  const preset = presetOf(params.get("range"));
  if (preset) return { preset: preset.value };

  const from = Date.parse(params.get("from") ?? "") / 1000;
  const to = Date.parse(params.get("to") ?? "") / 1000;
  return to - from >= MIN_WINDOW_SECONDS ? { from, to } : DEFAULT_RANGE;
}

/** The query string for a range; the default range needs none. */
export function rangeToParams(range: TimeRange): Record<string, string> {
  if ("preset" in range) return range.preset === DEFAULT_PRESET ? {} : { range: range.preset };
  return { from: new Date(range.from * 1000).toISOString(), to: new Date(range.to * 1000).toISOString() };
}

/** The window to ask the server for, in unix seconds. */
export function resolveRange(range: TimeRange, nowMs: number): { from: number; to: number } {
  if (!("preset" in range)) return range;
  const to = Math.floor(nowMs / 1000);
  return { from: to - (presetOf(range.preset)?.seconds ?? 3_600), to };
}

/** Live ranges follow new readings; a zoomed window stays put. */
export function refreshInterval(range: TimeRange): number | false {
  if (!("preset" in range)) return false;
  if (range.preset === "1h" || range.preset === "6h") return 30_000;
  return range.preset === "24h" ? 60_000 : 300_000;
}

const BUCKET_UNITS: [seconds: number, name: string][] = [
  [86_400, "day"],
  [3_600, "hour"],
  [60, "minute"],
  [1, "second"],
];

/** The width of one chart point in words: "15 seconds", "5 minutes", "1 hour". */
export function describeBucket(seconds: number): string {
  const [size, name] = BUCKET_UNITS.find(([unit]) => seconds >= unit && seconds % unit === 0) ?? [
    1,
    "second",
  ];
  const count = seconds / size;
  return `${count} ${name}${count === 1 ? "" : "s"}`;
}
