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

/** Recessive furniture: axis text in the muted text tone, gridlines one step off the surface. */
export const CHART_CHROME: Record<
  ChartScheme,
  { surface: string; axis: string; grid: string; cursor: string }
> = {
  light: { surface: "#ffffff", axis: "#6a7099", grid: "#eceef5", cursor: "#aab0c9" },
  dark: { surface: "#262c50", axis: "#8d93b9", grid: "#333a63", cursor: "#6a7099" },
};
