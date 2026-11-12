import { Button, Group, Paper, ScrollArea, Table, Text, Title, useComputedColorScheme } from "@mantine/core";
import { useEffect, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { formatTick, formatValue, niceTicks, type ChartSeries, type ChartUnit } from "./chartData";
import { CHART_CHROME } from "./chartPalette";
import classes from "./TimeSeriesChart.module.css";

/** One bar position on the x-axis. */
export interface BarCategory {
  key: string;
  /** Under the bar; may be skipped when there is no room. */
  label: string;
  /** In the tooltip and table: the full name. */
  title: string;
}

export interface BarChartProps {
  title: string;
  description?: string;
  categories: BarCategory[];
  /** One value per category; null is no reading. */
  series: ChartSeries[];
  unit: ChartUnit;
  /** Side by side, or piled up into one bar per category. */
  mode?: "grouped" | "stacked";
  /** Fixed top of the y-axis (100 for percentages). */
  max?: number;
  /** The highlighted category. */
  selected?: number;
  onSelect?: (index: number) => void;
  /** Controls at the top right, before the table toggle. */
  actions?: ReactNode;
  refreshing?: boolean;
}

const HEIGHT = 220;
const AXIS_WIDTH = 64;
const AXIS_HEIGHT = 22;
const TOP = 12;
const LABEL_SPACE = 52;

/**
 * Bars over categories such as days or months, grouped or stacked, in the charts' palette: hover or
 * arrow keys for a readout of every series, click to choose a category, and a table of the same numbers.
 */
export function BarChart(props: BarChartProps) {
  const { title, description, series, unit, actions, categories } = props;
  const [view, setView] = useState<"chart" | "table">("chart");

  return (
    <Paper component="figure" className="argus-surface" m={0} p="md" radius="sm" miw={0}>
      <Group justify="space-between" align="flex-start" wrap={actions ? "wrap" : "nowrap"} gap="sm">
        <div>
          <Title order={3} fz={15}>
            {title}
          </Title>
          {description && (
            <Text fz="xs" c="dimmed">
              {description}
            </Text>
          )}
        </div>
        <Group gap="xs" wrap="nowrap">
          {actions}
          <Button
            variant="subtle"
            size="compact-xs"
            onClick={() => setView(view === "chart" ? "table" : "chart")}
          >
            {view === "chart" ? "Show table" : "Show chart"}
          </Button>
        </Group>
      </Group>
      {series.length > 1 && (
        <Group gap="md" mt={6} wrap="wrap">
          {series.map((item) => (
            <Group key={item.key} gap={6} wrap="nowrap">
              <span
                style={{
                  width: 10,
                  height: 10,
                  borderRadius: 2,
                  background: item.color,
                  display: "inline-block",
                }}
                aria-hidden
              />
              <Text fz="xs">{item.label}</Text>
            </Group>
          ))}
        </Group>
      )}
      {view === "chart" ? (
        <Bars {...props} />
      ) : (
        <ScrollArea h={HEIGHT} mt="sm" type="auto" offsetScrollbars>
          <Table stickyHeader aria-label={title} fz="xs" verticalSpacing={4}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Period</Table.Th>
                {series.map((item) => (
                  <Table.Th key={item.key} ta="right">
                    {item.label}
                  </Table.Th>
                ))}
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {categories.map((category, index) => (
                <Table.Tr key={category.key}>
                  <Table.Td>{category.title}</Table.Td>
                  {series.map((item) => (
                    <Table.Td key={item.key} ta="right">
                      {formatValue(item.values[index], unit)}
                    </Table.Td>
                  ))}
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </ScrollArea>
      )}
    </Paper>
  );
}

function Bars({
  title,
  categories,
  series,
  unit,
  mode = "grouped",
  max,
  selected,
  onSelect,
  refreshing,
}: BarChartProps) {
  const scheme = useComputedColorScheme("light");
  const chrome = CHART_CHROME[scheme];
  const mount = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(600);
  const [hover, setHover] = useState<number | null>(null);

  useEffect(() => {
    const element = mount.current;
    if (!element) return;
    const measure = () => setWidth(element.clientWidth || 600);
    measure();
    const resize = new ResizeObserver(measure);
    resize.observe(element);
    return () => resize.disconnect();
  }, []);

  const totals = categories.map((_, index) =>
    mode === "stacked"
      ? series.reduce((sum, item) => sum + (item.values[index] ?? 0), 0)
      : Math.max(0, ...series.map((item) => item.values[index] ?? 0)),
  );
  const ticks = niceTicks(Math.max(0, ...totals), unit, max);
  const top = ticks[ticks.length - 1];

  const plotWidth = Math.max(1, width - AXIS_WIDTH);
  const plotHeight = HEIGHT - AXIS_HEIGHT - TOP;
  const band = plotWidth / Math.max(1, categories.length);
  const gap = Math.min(8, band * 0.2);
  const y = (value: number) => TOP + plotHeight - (value / top) * plotHeight;
  const labelEvery = Math.max(1, Math.ceil(LABEL_SPACE / band));
  const empty = !series.some((item) => item.values.some((value) => value != null && value > 0));

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const index = hover ?? selected ?? categories.length - 1;
    const moves: Record<string, () => void> = {
      ArrowLeft: () => setHover(Math.max(0, index - 1)),
      ArrowRight: () => setHover(Math.min(categories.length - 1, index + 1)),
      Home: () => setHover(0),
      End: () => setHover(categories.length - 1),
      Enter: () => onSelect?.(index),
      " ": () => onSelect?.(index),
      Escape: () => setHover(null),
    };
    const move = moves[event.key];
    if (!move) return;
    event.preventDefault();
    move();
  };

  const tooltipX = hover == null ? 0 : AXIS_WIDTH + band * (hover + 0.5);
  const flip = tooltipX > width * 0.6;

  return (
    <div
      ref={mount}
      className={refreshing ? `${classes.plot} ${classes.refreshing}` : classes.plot}
      tabIndex={0}
      role="group"
      aria-roledescription="bar chart"
      aria-label={`${title}. Arrow keys step through the periods${onSelect ? ", Enter chooses one" : ""}.`}
      onKeyDown={onKeyDown}
      onBlur={() => setHover(null)}
      style={{ height: HEIGHT }}
    >
      <svg width={width} height={HEIGHT} role="presentation" style={{ display: "block" }}>
        {ticks.map((tick) => (
          <g key={tick}>
            <line x1={AXIS_WIDTH} x2={width} y1={y(tick)} y2={y(tick)} stroke={chrome.grid} strokeWidth={1} />
            <text
              x={AXIS_WIDTH - 8}
              y={y(tick)}
              dy="0.32em"
              textAnchor="end"
              fill={chrome.axis}
              fontSize={12}
            >
              {formatTick(tick, unit)}
            </text>
          </g>
        ))}
        {categories.map((category, index) => {
          const x0 = AXIS_WIDTH + band * index;
          const inner = band - gap;
          let stackBase = 0;
          return (
            <g
              key={category.key}
              onMouseEnter={() => setHover(index)}
              onMouseLeave={() => setHover(null)}
              onClick={() => onSelect?.(index)}
              style={{ cursor: onSelect ? "pointer" : undefined }}
            >
              <rect
                x={x0}
                y={TOP}
                width={band}
                height={plotHeight}
                fill={
                  index === selected
                    ? "var(--mantine-color-iris-light)"
                    : index === hover
                      ? chrome.grid
                      : "transparent"
                }
              />
              {series.map((item, position) => {
                const value = item.values[index];
                if (value == null || value <= 0) return null;
                if (mode === "stacked") {
                  const rect = (
                    <rect
                      key={item.key}
                      x={x0 + gap / 2}
                      y={y(stackBase + value)}
                      width={Math.max(1, inner)}
                      height={Math.max(1, y(stackBase) - y(stackBase + value))}
                      fill={item.color}
                    />
                  );
                  stackBase += value;
                  return rect;
                }
                const barWidth = inner / series.length;
                return (
                  <rect
                    key={item.key}
                    x={x0 + gap / 2 + barWidth * position}
                    y={y(value)}
                    width={Math.max(1, barWidth - (series.length > 1 ? 1 : 0))}
                    height={Math.max(1, y(0) - y(value))}
                    rx={Math.min(2, barWidth / 4)}
                    fill={item.color}
                  />
                );
              })}
              {(categories.length - 1 - index) % labelEvery === 0 && (
                <text
                  // The newest label ends at the edge rather than running past it.
                  x={index === categories.length - 1 && band < LABEL_SPACE ? width - 2 : x0 + band / 2}
                  y={HEIGHT - 6}
                  textAnchor={index === categories.length - 1 && band < LABEL_SPACE ? "end" : "middle"}
                  fill={chrome.axis}
                  fontSize={12}
                  fontWeight={index === selected ? 600 : 400}
                >
                  {category.label}
                </text>
              )}
            </g>
          );
        })}
      </svg>
      {empty && (
        <div className={classes.empty}>
          <Text fz="sm" c="dimmed">
            No readings in this range
          </Text>
        </div>
      )}
      {hover != null && hover < categories.length && (
        <div
          className={classes.tooltip}
          aria-hidden
          style={{
            left: tooltipX,
            top: HEIGHT / 2 - 20,
            transform: flip ? "translate(calc(-100% - 14px), -50%)" : "translate(14px, -50%)",
          }}
        >
          <div className={classes.time}>{categories[hover].title}</div>
          {series.map((item) => (
            <div key={item.key} className={classes.row}>
              <span className={classes.key} style={{ background: item.color, height: 8, width: 8 }} />
              <span className={classes.value}>{formatValue(item.values[hover], unit)}</span>
              <span className={classes.label}>{item.label}</span>
            </div>
          ))}
          {mode === "stacked" && series.length > 1 && (
            <div className={classes.row}>
              <span className={classes.key} style={{ height: 8, width: 8 }} />
              <span className={classes.value}>{formatValue(totals[hover], unit)}</span>
              <span className={classes.label}>Total</span>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
