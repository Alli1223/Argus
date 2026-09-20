import type { HostTemperatures, MetricSeries } from "../api/types";
import type { ChartSeries } from "../components/charts/chartData";
import {
  MACHINE_HUES,
  SERIES_COLORS,
  machineShades,
  type ChartScheme,
} from "../components/charts/chartPalette";

export interface TemperatureChart {
  key: string;
  /** The device the sensors belong to, such as "coretemp" or "nvme0". */
  title: string;
  description?: string;
  series: ChartSeries[];
}

/** The most lines on one chart: one per categorical colour slot. */
export const MAX_SENSORS_PER_CHART = SERIES_COLORS.light.length;

interface Sensor {
  key: string;
  device: string;
  name: string;
}

// What kind of hardware a device is, from its driver's name; the first match wins.
const DEVICE_KINDS: [pattern: RegExp, kind: string][] = [
  [/^(coretemp|k8temp|k10temp|zenpower|x86_pkg_temp|cpu[_-]?thermal|soc[_-]?thermal)/i, "Processor"],
  [/^((amdgpu|radeon|nouveau|i915|xe)\b|gpu[_-]?thermal)/i, "Graphics"],
  [/^nvme\d/i, "NVMe drive"],
  [/^sd[a-z]+$/i, "Drive"],
  [/^(pch_|chipset)/i, "Chipset"],
  [/^(iwlwifi|mt7|ath\d|brcm|rtw)/i, "Wi-Fi"],
  [/^(nct\d|it87|w83|f71|asus|gigabyte)/i, "Motherboard"],
  [/^acpitz/i, "ACPI thermal zone"],
  [/^thermal$/, "Thermal zones"],
];

// Processors first, as the usual reason to look, then the other kinds in the order above.
const kindRank = (device: string) => {
  const index = DEVICE_KINDS.findIndex(([pattern]) => pattern.test(device));
  return index < 0 ? DEVICE_KINDS.length : index;
};

const natural = new Intl.Collator(undefined, { numeric: true, sensitivity: "base" });

/** "Core 12" and "Core 3" are one family, "Core"; "Package id 0" is a family of its own. */
const family = (name: string) => name.replace(/\s*\d+$/, "");

/** Splits a series key, `{device}/{sensor}`, at its first slash. */
export function parseSensorKey(key: string): Sensor {
  const slash = key.indexOf("/");
  return slash < 0
    ? { key, device: key, name: key }
    : { key, device: key.slice(0, slash), name: key.slice(slash + 1) };
}

/** What a device is, when its name says: "Processor", "NVMe drive". */
export function describeDevice(device: string): string | undefined {
  return DEVICE_KINDS.find(([pattern]) => pattern.test(device))?.[1];
}

/**
 * A device's sensors in reading order: one-off sensors such as "Package id 0" or "Composite" before
 * numbered families such as "Core 0…15", each in natural order.
 */
function orderSensors(sensors: Sensor[]): Sensor[] {
  const sizes = new Map<string, number>();
  for (const sensor of sensors) sizes.set(family(sensor.name), (sizes.get(family(sensor.name)) ?? 0) + 1);
  return [...sensors].sort(
    (a, b) => sizes.get(family(a.name))! - sizes.get(family(b.name))! || natural.compare(a.name, b.name),
  );
}

/** Phrases joined into one description: ["Processor", "sensors 1–6 of 17"] → "Processor, sensors 1–6 of 17". */
function describe(...phrases: (string | undefined)[]): string | undefined {
  const text = phrases.filter(Boolean).join(", ");
  return text ? text[0].toUpperCase() + text.slice(1) : undefined;
}

/** Splits a list into as few even parts as keep each within `size`: 17 by 8 gives 6, 6 and 5. */
function evenParts<T>(items: T[], size: number): T[][] {
  const parts = Math.ceil(items.length / size);
  const each = Math.ceil(items.length / parts);
  return Array.from({ length: parts }, (_, index) => items.slice(index * each, (index + 1) * each));
}

/**
 * A single combined chart of every sensor on every host. Each machine's sensors share a hue
 * family (shades of red, green, blue, …) so machines are easy to tell apart at a glance.
 */
export function fleetTemperatureChart(
  hosts: HostTemperatures[],
  scheme: ChartScheme,
): { time: number[]; series: ChartSeries[]; from: number; to: number } {
  if (hosts.length === 0) return { time: [], series: [], from: 0, to: 0 };

  // Merge all hosts' timestamps into one sorted union.
  const timeSet = new Set<number>();
  for (const host of hosts) {
    for (const t of host.history.time) timeSet.add(t);
  }
  const time = [...timeSet].sort((a, b) => a - b);
  const timeIndex = new Map(time.map((t, i) => [t, i]));

  const from = Math.min(...hosts.map((h) => Date.parse(h.history.from))) / 1000;
  const to = Math.max(...hosts.map((h) => Date.parse(h.history.to))) / 1000;

  const hues = MACHINE_HUES[scheme];
  const series: ChartSeries[] = [];

  for (let machineIdx = 0; machineIdx < hosts.length; machineIdx++) {
    const host = hosts[machineIdx];
    const hue = hues[machineIdx % hues.length];
    const sensors = Object.keys(host.history.series)
      .map(parseSensorKey)
      .sort((a, b) => natural.compare(a.key, b.key));
    const shades = machineShades(hue, sensors.length, scheme);

    for (let sensorIdx = 0; sensorIdx < sensors.length; sensorIdx++) {
      const sensor = sensors[sensorIdx];
      const rawValues = host.history.series[sensor.key];
      const hostTime = host.history.time;

      const values: (number | null)[] = new Array(time.length).fill(null);
      for (let i = 0; i < hostTime.length; i++) {
        const idx = timeIndex.get(hostTime[i]);
        if (idx !== undefined) values[idx] = rawValues[i];
      }

      series.push({
        key: `${host.hostId}/${sensor.key}`,
        label: `${host.displayName}: ${sensor.name}`,
        color: shades[sensorIdx],
        values,
      });
    }
  }

  return { time, series, from, to };
}

/**
 * One chart per device from a temperature history, processors first. A device with more sensors
 * than there are colour slots is split across charts, so no chart carries more than eight lines;
 * colours are taken in slot order on each chart.
 */
export function temperatureCharts(history: MetricSeries, scheme: ChartScheme): TemperatureChart[] {
  const colors = SERIES_COLORS[scheme];
  const devices = new Map<string, Sensor[]>();
  for (const sensor of Object.keys(history.series).map(parseSensorKey)) {
    devices.set(sensor.device, [...(devices.get(sensor.device) ?? []), sensor]);
  }

  return [...devices.entries()]
    .sort(([a], [b]) => kindRank(a) - kindRank(b) || natural.compare(a, b))
    .flatMap(([device, sensors]) => {
      const ordered = orderSensors(sensors);
      const parts = evenParts(ordered, MAX_SENSORS_PER_CHART);
      let first = 1;
      return parts.map((part, index) => {
        const last = first + part.length - 1;
        const range = parts.length > 1 ? `sensors ${first}–${last} of ${ordered.length}` : undefined;
        first = last + 1;
        return {
          key: `${device}#${index}`,
          title: device,
          description: describe(describeDevice(device), range),
          series: part.map((sensor, slot) => ({
            key: sensor.key,
            label: sensor.name,
            color: colors[slot],
            values: history.series[sensor.key],
          })),
        };
      });
    });
}
