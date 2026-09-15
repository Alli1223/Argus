import { Group, Stack, Text, Tooltip, UnstyledButton } from "@mantine/core";
import type { CSSProperties } from "react";
import { Link } from "react-router";
import type { HostSummary } from "../../api/types";
import { formatAgo, formatPercent } from "../../lib/format";
import { severityColor, severityOf, worst } from "../../lib/severity";
import classes from "./Watch.module.css";

type Reading = "disk" | "memory" | "cpu";

// Outside in. Radii leave a surface gap between the 6-unit strokes.
const RINGS: { key: Reading; label: string; radius: number }[] = [
  { key: "disk", label: "Fullest disk", radius: 43 },
  { key: "memory", label: "Memory", radius: 34 },
  { key: "cpu", label: "CPU", radius: 25 },
];
const STROKE = 6;

function readings(host: HostSummary): Record<Reading, number | null> {
  return {
    disk: host.latest?.diskUsedPercent ?? null,
    memory: host.latest?.memoryPercent ?? null,
    cpu: host.latest?.cpuPercent ?? null,
  };
}

/** Worst first: critical readings, then hosts that went quiet, then warnings, then the rest. */
function watchRank(host: HostSummary): number {
  if (host.status === "Offline") return 1;
  const severity = worst(Object.values(readings(host)).map(severityOf));
  return severity === "critical" ? 0 : severity === "warning" ? 2 : 3;
}

/** One host as an eye: rings for disk, memory and CPU; a pupil while it reports, a closed lid when not. */
export function EyeGlyph({ host, size = 76 }: { host: HostSummary; size?: number }) {
  const online = host.status === "Online";
  const values = readings(host);

  return (
    <svg viewBox="0 0 100 100" width={size} height={size} aria-hidden="true" className={classes.glyph}>
      {RINGS.map((ring) => {
        const value = values[ring.key];
        const shown = online && value != null;
        const color = shown ? severityColor[severityOf(value)] : "gray";
        const circumference = 2 * Math.PI * ring.radius;
        const fraction = shown ? Math.min(100, Math.max(0, value)) / 100 : 0;
        return (
          <g key={ring.key}>
            <circle
              cx="50"
              cy="50"
              r={ring.radius}
              fill="none"
              stroke={`var(--mantine-color-${color}-light)`}
              strokeWidth={STROKE}
            />
            {fraction > 0 && (
              <circle
                className={classes.arc}
                cx="50"
                cy="50"
                r={ring.radius}
                fill="none"
                stroke={`var(--mantine-color-${color}-filled)`}
                strokeWidth={STROKE}
                strokeLinecap="round"
                strokeDasharray={circumference}
                strokeDashoffset={circumference * (1 - fraction)}
                transform="rotate(-90 50 50)"
                style={{ "--argus-arc-length": circumference } as CSSProperties}
              />
            )}
          </g>
        );
      })}
      {online ? (
        <circle cx="50" cy="50" r="13" fill="var(--mantine-color-healthy-filled)" />
      ) : (
        <line
          x1="37"
          y1="50"
          x2="63"
          y2="50"
          stroke="var(--mantine-color-gray-5)"
          strokeWidth="4"
          strokeLinecap="round"
        />
      )}
    </svg>
  );
}

/** The reading that matters most for a host, as words: "CPU 93%", "Offline 3h ago". */
function headline(host: HostSummary, now: number): string {
  if (host.status === "Offline") return `Offline, ${formatAgo(host.lastSeenAt, now)}`;
  const values = readings(host);
  const candidates = RINGS.filter((ring) => values[ring.key] != null);
  if (candidates.length === 0) return "Waiting for data";
  const top = candidates.reduce((a, b) => {
    const rank = (ring: (typeof RINGS)[number]) =>
      ({ critical: 2, warning: 1, normal: 0 })[severityOf(values[ring.key])] * 1000 + (values[ring.key] ?? 0);
    return rank(b) > rank(a) ? b : a;
  });
  const label = top.key === "disk" ? "Disk" : top.label;
  return `${label} ${formatPercent(values[top.key])}`;
}

function TooltipBody({ host, now }: { host: HostSummary; now: number }) {
  const values = readings(host);
  return (
    <Stack gap={2}>
      <Text fz="sm" fw={600}>
        {host.displayName}
      </Text>
      {host.status === "Offline" ? (
        <Text fz="xs">Offline. Last report {formatAgo(host.lastSeenAt, now)}.</Text>
      ) : (
        RINGS.slice()
          .reverse()
          .map((ring) => (
            <Text key={ring.key} fz="xs" className="argus-data">
              <Text component="span" fw={700} fz="xs">
                {formatPercent(values[ring.key])}
              </Text>{" "}
              {ring.label.toLowerCase()}
            </Text>
          ))
      )}
    </Stack>
  );
}

export function Watch({ hosts, now }: { hosts: HostSummary[]; now: number }) {
  const ordered = [...hosts].sort(
    (a, b) => watchRank(a) - watchRank(b) || a.displayName.localeCompare(b.displayName),
  );

  return (
    <div className={classes.grid}>
      {ordered.map((host) => (
        <Tooltip
          key={host.id}
          label={<TooltipBody host={host} now={now} />}
          events={{ hover: true, focus: true, touch: false }}
          withArrow={false}
          multiline
        >
          <UnstyledButton
            component={Link}
            to={`/hosts/${host.id}`}
            className={classes.eye}
            aria-label={`${host.displayName}: ${headline(host, now)}`}
          >
            <EyeGlyph host={host} />
            <Text fz="sm" fw={600} truncate="end" maw="100%">
              {host.displayName}
            </Text>
            <Text fz="xs" c="dimmed" className="argus-data" truncate="end" maw="100%">
              {headline(host, now)}
            </Text>
          </UnstyledButton>
        </Tooltip>
      ))}
    </div>
  );
}

function Swatch({ color }: { color: string }) {
  return (
    <svg width="18" height="8" aria-hidden="true">
      <rect x="0" y="0" width="18" height="8" rx="4" fill={`var(--mantine-color-${color}-filled)`} />
    </svg>
  );
}

/** How to read the eyes: which ring is which, and what the colours mean. */
export function WatchLegend() {
  return (
    <Group gap="lg" className={classes.legend} wrap="wrap">
      <Text fz="xs" c="dimmed">
        Rings, outside in: fullest disk, memory, CPU
      </Text>
      <Group gap={6} wrap="nowrap">
        <Swatch color="iris" />
        <Text fz="xs" c="dimmed">
          Below 75%
        </Text>
      </Group>
      <Group gap={6} wrap="nowrap">
        <Swatch color="bronze" />
        <Text fz="xs" c="dimmed">
          75% or more
        </Text>
      </Group>
      <Group gap={6} wrap="nowrap">
        <Swatch color="crimson" />
        <Text fz="xs" c="dimmed">
          90% or more
        </Text>
      </Group>
      <Text fz="xs" c="dimmed">
        A closed eye has stopped reporting
      </Text>
    </Group>
  );
}
