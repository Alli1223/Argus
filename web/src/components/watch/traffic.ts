import { SERIES_COLORS, type ChartScheme } from "../charts/chartPalette";

/**
 * Traffic runs round a thin rim outside the rings: received down the left half, sent down the right,
 * both from the top. It is not a meter, so it takes the charts' identity colours, never a status one.
 */
export const RIM = { radius: 50.5, stroke: 3, gapDegrees: 14 };
// Traffic spans bytes to gigabits, so the rim is logarithmic: 1 kB/s barely shows, 1 Gbit/s fills a half.
const RIM_FULL_BYTES_PER_SECOND = 125_000_000;

/** Received is the charts' turquoise slot, sent their magenta one. */
export function trafficColors(scheme: ChartScheme) {
  const colors = SERIES_COLORS[scheme];
  return { received: colors[2], sent: colors[1] };
}

/** How much of its half a rate fills, 0 to 1. */
export function rimFraction(bytesPerSecond: number | null | undefined): number {
  if (bytesPerSecond == null || bytesPerSecond <= 0) return 0;
  const fraction = Math.log10(1 + bytesPerSecond / 1000) / Math.log10(1 + RIM_FULL_BYTES_PER_SECOND / 1000);
  return Math.min(1, fraction);
}
