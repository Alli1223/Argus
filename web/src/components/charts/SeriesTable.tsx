import { ScrollArea, Table, Text } from "@mantine/core";
import { formatPointTime, formatValue, type ChartSeries, type ChartUnit } from "./chartData";

interface SeriesTableProps {
  caption: string;
  time: number[];
  series: ChartSeries[];
  unit: ChartUnit;
  /** Matches the chart's height, so switching views does not move the page. */
  height: number;
}

/** A chart's numbers as a table, newest first: exact values without hovering. */
export function SeriesTable({ caption, time, series, unit, height }: SeriesTableProps) {
  const rows: number[] = [];
  for (let index = time.length - 1; index >= 0; index--) {
    if (series.some((item) => item.values[index] != null)) rows.push(index);
  }

  return (
    <ScrollArea h={height} mt="sm" type="auto" offsetScrollbars>
      {rows.length === 0 ? (
        <Text fz="sm" c="dimmed" py="md">
          No readings in this range.
        </Text>
      ) : (
        <Table stickyHeader aria-label={`${caption}, newest first`} fz="xs" verticalSpacing={4}>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Time</Table.Th>
              {series.map((item) => (
                <Table.Th key={item.key} ta="right">
                  {item.label}
                </Table.Th>
              ))}
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {rows.map((index) => (
              <Table.Tr key={time[index]}>
                <Table.Td>{formatPointTime(time[index])}</Table.Td>
                {series.map((item) => (
                  <Table.Td key={item.key} ta="right">
                    {formatValue(item.values[index], unit)}
                  </Table.Td>
                ))}
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      )}
    </ScrollArea>
  );
}
