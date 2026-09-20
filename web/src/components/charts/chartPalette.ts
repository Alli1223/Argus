// Chart colours, checked with the dataviz palette validator against the panel surfaces
// (see docs/ui-design.md). Series colours are for identity only; status colours never appear here.

export type ChartScheme = "light" | "dark";

type Slots = readonly [string, string, string, string, string, string, string, string];

/**
 * Categorical slots in fixed order: slot 1 is always a chart's main series. The order keeps neighbours
 * apart for colour-blind readers, so slots are taken in sequence and never skipped or reordered. Eight
 * is the most a chart gets; more series are split across charts.
 */
export const SERIES_COLORS: Record<ChartScheme, Slots> = {
  light: ["#4f5be0", "#d55181", "#0a8fb0", "#eb6834", "#c0569b", "#008300", "#4a3aa7", "#c98500"],
  dark: ["#7582ee", "#d55181", "#1f9fc4", "#d95926", "#c86aa8", "#3d9c3d", "#9085e9", "#c98500"],
};

/** Load averages are ordered, so they are steps of one hue with the 1-minute average strongest. */
export const LOAD_COLORS: Record<ChartScheme, { load1: string; load5: string; load15: string }> = {
  light: { load1: "#2c349c", load5: "#5f6de9", load15: "#95a0f5" },
  dark: { load1: "#dde1ff", load5: "#95a0f5", load15: "#5f6de9" },
};

/**
 * Base hues for per-machine color families on the combined temperatures chart.
 * Eight machines; more wrap (same hue, different shading — rare in practice).
 */
export const MACHINE_HUES: Record<ChartScheme, number[]> = {
  light: [0, 120, 210, 30, 270, 180, 330, 60],
  dark: [0, 130, 200, 35, 280, 175, 330, 55],
};

/** N shades of `hue` for one machine's sensor lines, stepping from dark to light. */
export function machineShades(hue: number, n: number, scheme: ChartScheme): string[] {
  const [lMin, lMax, sat] = scheme === "light" ? [32, 57, 74] : [45, 70, 62];
  if (n === 1) return [`hsl(${hue}, ${sat}%, ${Math.round((lMin + lMax) / 2)}%)`];
  return Array.from({ length: n }, (_, i) => {
    const l = Math.round(lMin + (i / (n - 1)) * (lMax - lMin));
    return `hsl(${hue}, ${sat}%, ${l}%)`;
  });
}

/** Recessive furniture: axis text in the muted text tone, gridlines one step off the surface. */
export const CHART_CHROME: Record<
  ChartScheme,
  { surface: string; axis: string; grid: string; cursor: string }
> = {
  light: { surface: "#ffffff", axis: "#6a7099", grid: "#eceef5", cursor: "#aab0c9" },
  dark: { surface: "#262c50", axis: "#8d93b9", grid: "#333a63", cursor: "#6a7099" },
};
