import {
  Alert,
  Anchor,
  Box,
  Code,
  Group,
  Paper,
  SegmentedControl,
  SimpleGrid,
  Skeleton,
  Table,
  Text,
  TextInput,
  useComputedColorScheme,
} from "@mantine/core";
import { IconAlertTriangle, IconSearch } from "@tabler/icons-react";
import { useMemo, useState, type ReactNode } from "react";
import { Link, useSearchParams } from "react-router";
import type { UnifiClientTotal, UnifiDevice, UnifiNetwork } from "../../api/types";
import { useUnifiClients, useUnifiHistory, useUnifiNetworks } from "../../api/unifi";
import { SERIES_COLORS } from "../../components/charts/chartPalette";
import { RangePicker } from "../../components/charts/RangePicker";
import { TimeSeriesChart } from "../../components/charts/TimeSeriesChart";
import { HostStatusBadge } from "../../components/HostBits";
import { PageHeader } from "../../components/PageHeader";
import { ErrorScreen, MessageScreen } from "../../components/Screens";
import { Section } from "../../components/Section";
import { UsageMeter } from "../../components/UsageMeter";
import { formatAgo, formatBytes, formatDuration, formatRate } from "../../lib/format";
import {
  describeBucket,
  rangeFromParams,
  rangeToParams,
  RANGE_PRESETS,
  type TimeRange,
} from "../../lib/timeRange";
import { useNow } from "../../lib/useNow";
import { clientMatches, deviceKind } from "./network";

/** A UniFi network: internet traffic, its gateways, switches and access points, and what each client used. */
export function NetworkPage() {
  const [params, setParams] = useSearchParams();
  const networks = useUnifiNetworks();

  if (networks.isError && !networks.data) {
    return <ErrorScreen error={networks.error} onRetry={() => void networks.refetch()} />;
  }

  const list = networks.data ?? [];
  const network = list.find((item) => item.hostId === params.get("host")) ?? list[0];
  const range = rangeFromParams(params);
  const show = (host: string | undefined, next: TimeRange) =>
    setParams(new URLSearchParams({ ...(host && list.length > 1 ? { host } : {}), ...rangeToParams(next) }), {
      replace: true,
    });

  return (
    <>
      <PageHeader
        title="Network"
        description="Your UniFi network: internet traffic, the gateways, switches and access points, and what each client used."
        actions={
          list.length > 1 && (
            <SegmentedControl
              aria-label="Network"
              value={network?.hostId}
              onChange={(host) => show(host, range)}
              data={list.map((item) => ({ value: item.hostId, label: item.site ?? item.hostName }))}
            />
          )
        }
      />
      {networks.isPending ? (
        <SimpleGrid cols={{ base: 2, sm: 4 }} spacing="md">
          {Array.from({ length: 4 }, (_, index) => (
            <Skeleton key={index} h={92} radius="sm" />
          ))}
        </SimpleGrid>
      ) : !network ? (
        <SetUp />
      ) : (
        <NetworkView
          key={network.hostId}
          network={network}
          range={range}
          onRange={(next) => show(network.hostId, next)}
        />
      )}
    </>
  );
}

/** How to point an agent at a UniFi console. */
function SetUp() {
  return (
    <MessageScreen title="No UniFi network yet">
      One agent on the network reads it from your UniFi OS console. Make an API key in UniFi Network under
      Settings → Control Plane → Integrations, save it in a file, and run that machine's install command again
      with <Code>--unifi https://192.168.1.1 --unifi-key-file ./unifi.key</Code>, using your console's
      address.
    </MessageScreen>
  );
}

interface NetworkViewProps {
  network: UnifiNetwork;
  range: TimeRange;
  onRange: (range: TimeRange) => void;
}

function NetworkView({ network, range, onRange }: NetworkViewProps) {
  const now = useNow(30_000);
  const online = network.devices.filter((device) => device.online).length;
  const current = network.now;

  return (
    <>
      {network.problem && (
        <Alert
          color="bronze"
          icon={<IconAlertTriangle size={18} />}
          title="The agent cannot read the UniFi console"
          mb="lg"
        >
          {network.problem}
        </Alert>
      )}
      <SimpleGrid cols={{ base: 2, sm: 4 }} spacing="md">
        <Tile label="Downloading" value={formatRate(current?.wanRxBytesPerSec)} note="Internet, right now" />
        <Tile label="Uploading" value={formatRate(current?.wanTxBytesPerSec)} note="Internet, right now" />
        <Tile
          label="Clients"
          value={
            current?.wiredClients == null && current?.wirelessClients == null
              ? "–"
              : String((current?.wiredClients ?? 0) + (current?.wirelessClients ?? 0))
          }
          note={`${current?.wiredClients ?? 0} wired, ${current?.wirelessClients ?? 0} wireless${current?.guestClients ? `, ${current.guestClients} guests` : ""}`}
        />
        <Tile
          label="Devices online"
          value={network.devices.length === 0 ? "–" : `${online} of ${network.devices.length}`}
          note={
            <>
              Read by{" "}
              <Anchor component={Link} to={`/hosts/${network.hostId}`} fz="xs">
                {network.hostName}
              </Anchor>{" "}
              {formatAgo(network.checkedAt, now)}
            </>
          }
          warn={online < network.devices.length}
        />
      </SimpleGrid>

      <Box mt="xl">
        <History network={network} range={range} onRange={onRange} />
      </Box>

      <Section
        title="Devices"
        mt="xl"
        action={
          network.version && (
            <Text fz="xs" c="dimmed">
              UniFi Network {network.version}
            </Text>
          )
        }
      >
        <DeviceTable devices={network.devices} now={now} />
      </Section>

      <Clients network={network} range={range} />
    </>
  );
}

function Tile({
  label,
  value,
  note,
  warn,
}: {
  label: string;
  value: string;
  note?: ReactNode;
  warn?: boolean;
}) {
  return (
    <Paper className="argus-surface" p="md" radius="sm">
      <Text fz="xs" c="dimmed" fw={500}>
        {label}
      </Text>
      <Text fz={22} fw={600} className="argus-data" mt={2} c={warn ? "crimson" : undefined}>
        {value}
      </Text>
      {note && (
        <Text fz="xs" c="dimmed" className="argus-data">
          {note}
        </Text>
      )}
    </Paper>
  );
}

function History({ network, range, onRange }: NetworkViewProps) {
  const scheme = useComputedColorScheme("light");
  const palette = SERIES_COLORS[scheme];
  const history = useUnifiHistory(network.hostId, range);
  const zoom = (from: number, to: number) => onRange({ from: Math.floor(from), to: Math.ceil(to) });

  const data = history.data;
  const from = data ? Date.parse(data.from) / 1000 : 0;
  const to = data ? Date.parse(data.to) / 1000 : 0;

  return (
    <>
      <RangePicker range={range} onChange={onRange}>
        {data && (
          <Text fz="xs" c="dimmed">
            One point per {describeBucket(data.bucketSeconds)}. Drag across a chart to zoom in.
          </Text>
        )}
      </RangePicker>
      {!data ? (
        <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="md" mt="md">
          <Skeleton h={296} radius="sm" />
          <Skeleton h={296} radius="sm" />
        </SimpleGrid>
      ) : (
        <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="md" mt="md">
          <TimeSeriesChart
            title="Internet"
            description="Traffic through the gateway's WAN"
            time={data.time}
            unit="bytesPerSecond"
            from={from}
            to={to}
            refreshing={history.isPlaceholderData}
            onZoom={zoom}
            series={[
              { key: "rx", label: "Download", color: palette[2], values: data.series.wanRx ?? [] },
              { key: "tx", label: "Upload", color: palette[1], values: data.series.wanTx ?? [] },
            ]}
          />
          <TimeSeriesChart
            title="Clients"
            description="Connected clients, averaged over each point"
            time={data.time}
            unit="count"
            from={from}
            to={to}
            refreshing={history.isPlaceholderData}
            onZoom={zoom}
            series={[
              { key: "wireless", label: "Wireless", color: palette[0], values: data.series.wireless ?? [] },
              { key: "wired", label: "Wired", color: palette[1], values: data.series.wired ?? [] },
              { key: "guests", label: "Guests", color: palette[2], values: data.series.guests ?? [] },
            ]}
          />
        </SimpleGrid>
      )}
    </>
  );
}

function DeviceTable({ devices, now }: { devices: UnifiDevice[]; now: number }) {
  if (devices.length === 0) {
    return (
      <Text c="dimmed" fz="sm" p="md">
        No devices reported yet. The agent lists them about once a minute.
      </Text>
    );
  }

  return (
    <Table miw={900} highlightOnHover>
      <Table.Thead>
        <Table.Tr>
          <Table.Th>Device</Table.Th>
          <Table.Th>Status</Table.Th>
          <Table.Th>Model</Table.Th>
          <Table.Th>Uptime</Table.Th>
          <Table.Th>CPU</Table.Th>
          <Table.Th>Memory</Table.Th>
          <Table.Th>Clients</Table.Th>
          <Table.Th>Received</Table.Th>
          <Table.Th>Sent</Table.Th>
        </Table.Tr>
      </Table.Thead>
      <Table.Tbody>
        {devices.map((device) => (
          <Table.Tr key={device.mac}>
            <Table.Td>
              <Text fz="sm" fw={500}>
                {device.name}
              </Text>
              <Text fz="xs" c="dimmed">
                {deviceKind(device.type)}
              </Text>
            </Table.Td>
            <Table.Td>
              <HostStatusBadge status={device.online ? "Online" : "Offline"} />
              {!device.online && (
                <Text fz="xs" c="dimmed">
                  since {formatAgo(device.stateSince, now)}
                </Text>
              )}
            </Table.Td>
            <Table.Td>{device.model ?? "–"}</Table.Td>
            <Table.Td>{device.online ? formatDuration(device.uptimeSeconds) : "–"}</Table.Td>
            <Table.Td>
              <UsageMeter
                value={device.online ? device.cpuPercent : null}
                label={`${device.name} CPU`}
                width={48}
              />
            </Table.Td>
            <Table.Td>
              <UsageMeter
                value={device.online ? device.memoryPercent : null}
                label={`${device.name} memory`}
                width={48}
              />
            </Table.Td>
            <Table.Td>{device.clients ?? "–"}</Table.Td>
            <Table.Td>{formatRate(device.rxBytesPerSec)}</Table.Td>
            <Table.Td>{formatRate(device.txBytesPerSec)}</Table.Td>
          </Table.Tr>
        ))}
      </Table.Tbody>
    </Table>
  );
}

const SHOWN = 25;

/** Every client's downloads and uploads over the range, busiest first, with a bar for each against the busiest. */
function Clients({ network, range }: { network: UnifiNetwork; range: TimeRange }) {
  const scheme = useComputedColorScheme("light");
  const palette = SERIES_COLORS[scheme];
  const clients = useUnifiClients(network.hostId, range);
  const [search, setSearch] = useState("");
  const [all, setAll] = useState(false);
  const now = useNow(30_000);

  const rows = useMemo(
    () => (clients.data ?? []).filter((client) => clientMatches(client, search)),
    [clients.data, search],
  );
  const shown = all || search ? rows : rows.slice(0, SHOWN);
  const busiest = Math.max(
    1,
    ...rows.map((client) => Math.max(client.downloadedBytes, client.uploadedBytes)),
  );
  const rangeName =
    "preset" in range
      ? RANGE_PRESETS.find((preset) => preset.value === range.preset)?.name.toLowerCase()
      : "the window chosen";

  return (
    <Section
      title={`Clients, ${rangeName}`}
      mt="xl"
      action={
        <TextInput
          size="xs"
          aria-label="Search clients"
          placeholder="Name, IP or MAC"
          leftSection={<IconSearch size={14} />}
          value={search}
          onChange={(event) => setSearch(event.currentTarget.value)}
          w={220}
        />
      }
    >
      <Text size="sm" c="dimmed" mb="sm">
        Includes traffic between machines on your network, not only the internet.
      </Text>
      <Table miw={860} highlightOnHover>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>Client</Table.Th>
            <Table.Th>Connection</Table.Th>
            <Table.Th>Downloaded</Table.Th>
            <Table.Th>Uploaded</Table.Th>
            <Table.Th>Last seen</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {clients.isPending && (
            <Table.Tr>
              <Table.Td colSpan={5}>
                <Skeleton h={28} />
              </Table.Td>
            </Table.Tr>
          )}
          {clients.isSuccess && rows.length === 0 && (
            <Table.Tr>
              <Table.Td colSpan={5}>
                <Text c="dimmed" fz="sm" py="sm">
                  {search
                    ? "No clients match."
                    : "No client traffic in this range yet. The agent reports it about once a minute."}
                </Text>
              </Table.Td>
            </Table.Tr>
          )}
          {shown.map((client) => (
            <ClientRow
              key={client.mac}
              client={client}
              busiest={busiest}
              colors={[palette[2], palette[1]]}
              now={now}
            />
          ))}
          {!all && !search && rows.length > SHOWN && (
            <Table.Tr>
              <Table.Td colSpan={5}>
                <Anchor component="button" fz="sm" onClick={() => setAll(true)}>
                  Show all {rows.length} clients
                </Anchor>
              </Table.Td>
            </Table.Tr>
          )}
        </Table.Tbody>
      </Table>
    </Section>
  );
}

interface ClientRowProps {
  client: UnifiClientTotal;
  busiest: number;
  colors: [download: string, upload: string];
  now: number;
}

function ClientRow({ client, busiest, colors, now }: ClientRowProps) {
  const bar = (value: number, color: string) => (
    <Group gap={8} wrap="nowrap">
      <Text fz="sm" w={72} ta="right">
        {formatBytes(value)}
      </Text>
      <Box
        w={96}
        h={6}
        style={{ flexShrink: 0, borderRadius: 3, background: "var(--argus-border)" }}
        aria-hidden
      >
        <Box
          h="100%"
          style={{
            width: `${Math.max(value > 0 ? 2 : 0, (100 * value) / busiest)}%`,
            borderRadius: 3,
            background: color,
          }}
        />
      </Box>
    </Group>
  );

  return (
    <Table.Tr>
      <Table.Td>
        <Text fz="sm" fw={500}>
          {client.name}
        </Text>
        <Text fz="xs" c="dimmed" className="argus-data">
          {client.ip ?? client.mac}
        </Text>
      </Table.Td>
      <Table.Td>{client.wired ? "Wired" : (client.network ?? "Wireless")}</Table.Td>
      <Table.Td>{bar(client.downloadedBytes, colors[0])}</Table.Td>
      <Table.Td>{bar(client.uploadedBytes, colors[1])}</Table.Td>
      <Table.Td>{formatAgo(client.lastSeen, now)}</Table.Td>
    </Table.Tr>
  );
}
