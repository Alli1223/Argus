import { Anchor, Box, Group, SimpleGrid, Skeleton, Text, Title, useComputedColorScheme } from "@mantine/core";
import { Fragment, useMemo } from "react";
import { Link, useSearchParams } from "react-router";
import { useHosts } from "../../api/hosts";
import { useFleetTemperatures } from "../../api/metrics";
import type { HostStatus, HostTemperatures } from "../../api/types";
import { RangePicker } from "../../components/charts/RangePicker";
import { TimeSeriesChart } from "../../components/charts/TimeSeriesChart";
import { HostStatusBadge } from "../../components/HostBits";
import { PageHeader } from "../../components/PageHeader";
import { ErrorScreen, MessageScreen } from "../../components/Screens";
import { fleetTemperatureChart, temperatureCharts } from "../../lib/temperatures";
import { describeBucket, rangeFromParams, rangeToParams, type TimeRange } from "../../lib/timeRange";

/** Every temperature sensor of every host, one block of charts per host. */
export function TemperaturesPage() {
  const [params, setParams] = useSearchParams();
  const range = rangeFromParams(params);
  const fleet = useFleetTemperatures(range);
  const hosts = useHosts();

  const changeRange = (next: TimeRange) => setParams(rangeToParams(next), { replace: true });
  // Zooming adds a history entry, so Back returns to the wider view.
  const zoom = (from: number, to: number) =>
    setParams(rangeToParams({ from: Math.floor(from), to: Math.ceil(to) }));

  if (fleet.isError && !fleet.data) {
    return <ErrorScreen error={fleet.error} onRetry={() => void fleet.refetch()} />;
  }

  const reporting = fleet.data ?? [];
  const status = new Map(hosts.data?.map((host) => [host.id, host.status]));
  const silent =
    fleet.data && hosts.data
      ? hosts.data.filter((host) => !reporting.some((item) => item.hostId === host.id))
      : [];

  return (
    <>
      <PageHeader
        title="Temperatures"
        description="Every sensor on every host. Each point is a sensor's hottest reading in that time."
      />
      <RangePicker range={range} onChange={changeRange}>
        {reporting.length > 0 && (
          <Text fz="xs" c="dimmed">
            One point per {describeBucket(reporting[0].history.bucketSeconds)}. Drag across a chart to zoom
            in.
          </Text>
        )}
      </RangePicker>

      {fleet.isPending ? (
        <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="md" mt="xl">
          {Array.from({ length: 4 }, (_, index) => (
            <Skeleton key={index} h={296} radius="sm" />
          ))}
        </SimpleGrid>
      ) : reporting.length === 0 ? (
        <MessageScreen title="No temperatures in this range">
          Agents report temperatures from version 0.3.0, from the sensors a machine exposes: hardware
          monitoring chips on Linux and ACPI thermal zones on Windows. Virtual machines usually have none.
        </MessageScreen>
      ) : (
        <>
          <AllTemperaturesChart hosts={reporting} refreshing={fleet.isPlaceholderData} onZoom={zoom} />
          {reporting.map((host) => (
            <HostTemperatureCharts
              key={host.hostId}
              host={host}
              status={status.get(host.hostId)}
              refreshing={fleet.isPlaceholderData}
              onZoom={zoom}
            />
          ))}
        </>
      )}

      {silent.length > 0 && (
        <Text fz="sm" c="dimmed" mt="xl">
          No temperatures in this range from{" "}
          {silent.map((host, index) => (
            <Fragment key={host.id}>
              {index > 0 && ", "}
              <Anchor component={Link} to={`/hosts/${host.id}`} fz="sm">
                {host.displayName}
              </Anchor>
            </Fragment>
          ))}
          .
        </Text>
      )}
    </>
  );
}

interface AllTemperaturesChartProps {
  hosts: HostTemperatures[];
  refreshing: boolean;
  onZoom: (from: number, to: number) => void;
}

function AllTemperaturesChart({ hosts, refreshing, onZoom }: AllTemperaturesChartProps) {
  const scheme = useComputedColorScheme("light");
  const chart = useMemo(() => fleetTemperatureChart(hosts, scheme), [hosts, scheme]);

  return (
    <Box mt="xl">
      <TimeSeriesChart
        title="All temperatures"
        description="Each machine's sensors share a colour family"
        time={chart.time}
        series={chart.series}
        unit="celsius"
        from={chart.from}
        to={chart.to}
        refreshing={refreshing}
        onZoom={onZoom}
      />
    </Box>
  );
}

interface HostTemperatureChartsProps {
  host: HostTemperatures;
  status: HostStatus | undefined;
  refreshing: boolean;
  onZoom: (from: number, to: number) => void;
}

function HostTemperatureCharts({ host, status, refreshing, onZoom }: HostTemperatureChartsProps) {
  const scheme = useComputedColorScheme("light");
  const charts = useMemo(() => temperatureCharts(host.history, scheme), [host.history, scheme]);
  const from = Date.parse(host.history.from) / 1000;
  const to = Date.parse(host.history.to) / 1000;

  return (
    <Box component="section" mt="xl">
      <Group gap="md" mb="sm" align="baseline">
        <Title order={2} fz={17}>
          <Anchor component={Link} to={`/hosts/${host.hostId}`} inherit>
            {host.displayName}
          </Anchor>
        </Title>
        {status && <HostStatusBadge status={status} />}
      </Group>
      <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="md">
        {charts.map(({ key, ...chart }) => (
          <TimeSeriesChart
            key={key}
            {...chart}
            time={host.history.time}
            unit="celsius"
            from={from}
            to={to}
            refreshing={refreshing}
            onZoom={onZoom}
          />
        ))}
      </SimpleGrid>
    </Box>
  );
}
