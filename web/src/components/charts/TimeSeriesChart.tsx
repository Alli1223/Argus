import { Button, Group, Paper, Text, Title, useComputedColorScheme } from "@mantine/core";
import {
  useEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
  type FocusEvent,
  type KeyboardEvent,
} from "react";
import uPlot from "uplot";
import "uplot/dist/uPlot.min.css";
import {
  BYTE_INCREMENTS,
  formatAxisTime,
  formatPointTime,
  formatTick,
  formatValue,
  type ChartSeries,
  type ChartUnit,
} from "./chartData";
import { CHART_CHROME, type ChartScheme } from "./chartPalette";
import { SeriesTable } from "./SeriesTable";
import classes from "./TimeSeriesChart.module.css";

export interface TimeSeriesChartProps {
  title: string;
  /** A line under the title on what is plotted. */
  description?: string;
  /** Unix seconds, one per point. */
  time: number[];
  series: ChartSeries[];
  unit: ChartUnit;
  /** Fixed top of the y-axis (100 for percentages); otherwise it follows the data. */
  max?: number;
  /** The window on show, in unix seconds, so stretches without readings show as gaps. */
  from: number;
  to: number;
  /** True while a different range loads: the chart keeps its current render, dimmed. */
  refreshing?: boolean;
  /** Receives a window dragged out across the plot, in unix seconds. */
  onZoom?: (from: number, to: number) => void;
}

/** Canvas height, x-axis labels included, so the frame never needs its own scrollbar. */
const HEIGHT = 200;
const AXIS_FONT = '12px "Archivo Variable", "Archivo", system-ui, sans-serif';

/**
 * A line chart over time: one y-axis, 2px lines, a crosshair that snaps to the nearest reading with a
 * readout of every series, a legend for two or more series, and a table view of the same numbers.
 */
export function TimeSeriesChart(props: TimeSeriesChartProps) {
  const { title, description, time, series, unit } = props;
  const [view, setView] = useState<"chart" | "table">("chart");

  return (
    <Paper component="figure" className="argus-surface" m={0} p="md" radius="sm" miw={0}>
      <Group justify="space-between" align="flex-start" wrap="nowrap" gap="sm">
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
        <Button
          variant="subtle"
          size="compact-xs"
          onClick={() => setView(view === "chart" ? "table" : "chart")}
        >
          {view === "chart" ? "Show table" : "Show chart"}
        </Button>
      </Group>
      {series.length > 1 && <Legend series={series} unit={unit} />}
      {view === "chart" ? (
        <Plot {...props} />
      ) : (
        <SeriesTable caption={title} time={time} series={series} unit={unit} height={HEIGHT} />
      )}
    </Paper>
  );
}

function lastReading(values: (number | null)[]): number | null {
  for (let index = values.length - 1; index >= 0; index--) {
    if (values[index] != null) return values[index];
  }
  return null;
}

/** Identity for two or more series: a line key, the name, and the newest value as the line's end label. */
function Legend({ series, unit }: { series: ChartSeries[]; unit: ChartUnit }) {
  return (
    <Group gap="md" mt={6} wrap="wrap">
      {series.map((item) => (
        <Group key={item.key} gap={6} wrap="nowrap">
          <span className={classes.key} style={{ background: item.color }} aria-hidden />
          <Text fz="xs">{item.label}</Text>
          <Text fz="xs" c="dimmed" className="argus-data">
            {formatValue(lastReading(item.values), unit)}
          </Text>
        </Group>
      ))}
    </Group>
  );
}

/** What uPlot is built from. It is rebuilt when this changes and only given new data otherwise. */
interface PlotShape {
  series: [label: string, color: string][];
  unit: ChartUnit;
  max: number | null;
  scheme: ChartScheme;
  zoomable: boolean;
}

interface Hover {
  index: number;
  /** Crosshair position within the plot box, in CSS pixels. */
  x: number;
  y: number;
  width: number;
}

function summarize(series: ChartSeries[], unit: ChartUnit): string {
  return series
    .map((item) => {
      const values = item.values.filter((value): value is number => value != null);
      if (values.length === 0) return `${item.label}: no readings.`;
      const latest = formatValue(values[values.length - 1], unit);
      return `${item.label}: latest ${latest}, highest ${formatValue(Math.max(...values), unit)}.`;
    })
    .join(" ");
}

function Plot({
  title,
  time,
  series,
  unit,
  max,
  from,
  to,
  refreshing = false,
  onZoom,
}: TimeSeriesChartProps) {
  const scheme = useComputedColorScheme("light");
  const mount = useRef<HTMLDivElement>(null);
  const plot = useRef<uPlot | null>(null);
  const [hover, setHover] = useState<Hover | null>(null);

  const data = useMemo<uPlot.AlignedData>(() => [time, ...series.map((item) => item.values)], [time, series]);
  const shape = JSON.stringify({
    series: series.map((item) => [item.label, item.color]),
    unit,
    max: max ?? null,
    scheme,
    zoomable: onZoom !== undefined,
  } satisfies PlotShape);

  // uPlot's callbacks outlive the render that created them, so they read the newest values from here.
  const latest = useRef({ data, from, to, onZoom });
  useEffect(() => {
    latest.current = { data, from, to, onZoom };
  }, [data, from, to, onZoom]);

  useEffect(() => {
    const element = mount.current;
    if (!element) return;
    const config = JSON.parse(shape) as PlotShape;
    const chrome = CHART_CHROME[config.scheme];
    const axis: uPlot.Axis = {
      stroke: chrome.axis,
      font: AXIS_FONT,
      grid: { stroke: chrome.grid, width: 1 },
      ticks: { show: false },
    };

    const chart = new uPlot(
      {
        width: element.clientWidth,
        height: HEIGHT,
        legend: { show: false },
        padding: [12, 12, 0, 0],
        cursor: {
          y: false,
          // The crosshair snaps to the nearest reading; readers aim at a time, not at a 2px line.
          move: (self, left, top) => {
            if (left < 0) return [left, top];
            const x = self.data[0][self.posToIdx(left)];
            return [x == null ? left : self.valToPos(x, "x"), top];
          },
          points: {
            size: 8,
            width: 2,
            stroke: chrome.surface,
            fill: (_self, index) => config.series[index - 1]?.[1] ?? chrome.axis,
          },
          drag: { x: config.zoomable, y: false, setScale: false },
        },
        scales: {
          x: { time: true, range: () => [latest.current.from, latest.current.to] },
          // From zero, unless readings go below it (temperatures can).
          y: {
            range: (_self, dataMin, dataMax) => [
              dataMin < 0 ? dataMin * 1.12 : 0,
              config.max ?? (dataMax > 0 ? dataMax * 1.12 : 1),
            ],
          },
        },
        axes: [
          {
            ...axis,
            space: 80,
            values: (_self, splits, _axis, _space, step) =>
              splits.map((value) => formatAxisTime(value, step)),
          },
          {
            ...axis,
            size: 64,
            space: 32,
            incrs: config.unit === "bytesPerSecond" ? BYTE_INCREMENTS : undefined,
            values: (_self, splits) => splits.map((value) => formatTick(value, config.unit)),
          },
        ],
        series: [
          {},
          ...config.series.map(([label, color]) => ({
            label,
            stroke: color,
            width: 2,
            points: { show: false },
          })),
        ],
        hooks: {
          setCursor: [
            (self) => {
              const { idx, left, top } = self.cursor;
              setHover(
                idx == null || left == null || left < 0
                  ? null
                  : {
                      index: idx,
                      x: self.over.offsetLeft + left,
                      y: self.over.offsetTop + (top ?? 0),
                      width: element.clientWidth,
                    },
              );
            },
          ],
          setSelect: [
            (self) => {
              const { left, width } = self.select;
              if (width > 4)
                latest.current.onZoom?.(self.posToVal(left, "x"), self.posToVal(left + width, "x"));
              self.setSelect({ left: 0, top: 0, width: 0, height: 0 }, false);
            },
          ],
        },
      },
      latest.current.data,
      element,
    );
    plot.current = chart;

    const resize = new ResizeObserver(() => chart.setSize({ width: element.clientWidth, height: HEIGHT }));
    resize.observe(element);
    return () => {
      resize.disconnect();
      chart.destroy();
      plot.current = null;
      setHover(null);
    };
  }, [shape]);

  useEffect(() => {
    plot.current?.setData(data);
  }, [data]);

  const newestIndex = () => {
    for (let index = time.length - 1; index >= 0; index--) {
      if (series.some((item) => item.values[index] != null)) return index;
    }
    return time.length - 1;
  };

  const showIndex = (index: number) => {
    const chart = plot.current;
    if (!chart || time.length === 0) return;
    const clamped = Math.min(time.length - 1, Math.max(0, index));
    chart.setCursor({ left: chart.valToPos(time[clamped], "x"), top: chart.over.clientHeight / 2 });
  };

  const hide = () => plot.current?.setCursor({ left: -10, top: -10 });

  // The keyboard gets the same readout as the pointer: arrows step through readings.
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const index = hover?.index ?? newestIndex();
    const step = event.shiftKey ? 10 : 1;
    const moves: Record<string, () => void> = {
      ArrowLeft: () => showIndex(index - step),
      ArrowRight: () => showIndex(index + step),
      Home: () => showIndex(0),
      End: () => showIndex(time.length - 1),
      Escape: hide,
    };
    const move = moves[event.key];
    if (!move) return;
    event.preventDefault();
    move();
  };

  const onFocus = (event: FocusEvent<HTMLDivElement>) => {
    if (event.currentTarget.matches(":focus-visible")) showIndex(newestIndex());
  };

  const empty = !series.some((item) => item.values.some((value) => value != null));

  return (
    <div
      className={refreshing ? `${classes.plot} ${classes.refreshing}` : classes.plot}
      style={{ "--chart-cursor": CHART_CHROME[scheme].cursor } as CSSProperties}
      tabIndex={0}
      role="group"
      aria-roledescription="chart"
      aria-label={`${title}. ${summarize(series, unit)} Arrow keys step through the readings.`}
      onKeyDown={onKeyDown}
      onFocus={onFocus}
      onBlur={hide}
    >
      <div ref={mount} style={{ height: HEIGHT }} />
      {empty && (
        <div className={classes.empty}>
          <Text fz="sm" c="dimmed">
            No readings in this range
          </Text>
        </div>
      )}
      {hover && <Tooltip hover={hover} time={time} series={series} unit={unit} />}
    </div>
  );
}

/** Every series at the crosshair: the value leads, the series name follows. */
function Tooltip({
  hover,
  time,
  series,
  unit,
}: {
  hover: Hover;
  time: number[];
  series: ChartSeries[];
  unit: ChartUnit;
}) {
  if (hover.index >= time.length) return null;
  const flip = hover.x > hover.width * 0.6;
  const top = Math.min(Math.max(hover.y, 40), HEIGHT - 60);

  return (
    <div
      className={classes.tooltip}
      aria-hidden
      style={{
        left: hover.x,
        top,
        transform: flip ? "translate(calc(-100% - 14px), -50%)" : "translate(14px, -50%)",
      }}
    >
      <div className={classes.time}>{formatPointTime(time[hover.index])}</div>
      {series.map((item) => (
        <div key={item.key} className={classes.row}>
          <span className={classes.key} style={{ background: item.color }} />
          <span className={classes.value}>{formatValue(item.values[hover.index], unit)}</span>
          <span className={classes.label}>{item.label}</span>
        </div>
      ))}
    </div>
  );
}
