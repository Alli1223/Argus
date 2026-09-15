export interface SparklineLayout {
  /** SVG path data; gaps in the readings break the line. */
  path: string;
  /** Where the newest reading sits, for its end dot. */
  end: { x: number; y: number } | null;
}

const round = (value: number) => Math.round(value * 10) / 10;

/**
 * Lays out readings as a sparkline in a `width` × `height` box. The vertical scale spans at least
 * `minSpan`, so noise on a nearly flat series does not read as a trend.
 */
export function layoutSparkline(
  values: (number | null)[],
  width: number,
  height: number,
  minSpan = 2,
  inset = 6,
): SparklineLayout {
  const readings = values.filter((value): value is number => value != null);
  if (readings.length === 0) return { path: "", end: null };

  let low = Math.min(...readings);
  let high = Math.max(...readings);
  if (high - low < minSpan) {
    const middle = (high + low) / 2;
    low = middle - minSpan / 2;
    high = middle + minSpan / 2;
  }

  const x = (index: number) =>
    values.length === 1 ? width / 2 : inset + (index * (width - 2 * inset)) / (values.length - 1);
  const y = (value: number) => inset + ((high - value) / (high - low || 1)) * (height - 2 * inset);

  let path = "";
  let drawing = false;
  let end: SparklineLayout["end"] = null;
  for (let index = 0; index < values.length; index++) {
    const value = values[index];
    if (value == null) {
      drawing = false;
      continue;
    }
    const point = { x: round(x(index)), y: round(y(value)) };
    path += `${drawing ? "L" : "M"}${point.x} ${point.y}`;
    drawing = true;
    end = point;
  }
  return { path, end };
}
