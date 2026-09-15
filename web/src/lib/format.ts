const BYTE_UNITS = ["B", "KB", "MB", "GB", "TB", "PB"];
const MISSING = "–";

function isNumber(value: number | null | undefined): value is number {
  return value != null && Number.isFinite(value);
}

/** Byte counts with binary multiples: 1536 → "1.5 KB". */
export function formatBytes(bytes: number | null | undefined, digits = 1): string {
  if (!isNumber(bytes)) return MISSING;
  let value = Math.abs(bytes);
  let unit = 0;
  while (value >= 1024 && unit < BYTE_UNITS.length - 1) {
    value /= 1024;
    unit++;
  }
  const sign = bytes < 0 ? "-" : "";
  return `${sign}${value.toFixed(unit === 0 ? 0 : digits)} ${BYTE_UNITS[unit]}`;
}

/** Throughput: 2048 → "2.0 KB/s". */
export function formatRate(bytesPerSecond: number | null | undefined): string {
  return isNumber(bytesPerSecond) ? `${formatBytes(bytesPerSecond)}/s` : MISSING;
}

export function formatPercent(value: number | null | undefined, digits = 0): string {
  return isNumber(value) ? `${value.toFixed(digits)}%` : MISSING;
}

/** Coarse durations for uptimes and ages: "3d 4h", "5h 12m", "42m", "30s". */
export function formatDuration(seconds: number | null | undefined): string {
  if (!isNumber(seconds)) return MISSING;
  const total = Math.max(0, Math.floor(seconds));
  const days = Math.floor(total / 86_400);
  const hours = Math.floor((total % 86_400) / 3_600);
  const minutes = Math.floor((total % 3_600) / 60);
  if (days > 0) return `${days}d ${hours}h`;
  if (hours > 0) return `${hours}h ${minutes}m`;
  if (minutes > 0) return `${minutes}m`;
  return `${total}s`;
}
