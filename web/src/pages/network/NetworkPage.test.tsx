import { screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { MetricSeries, UnifiClientTotal, UnifiNetwork } from "../../api/types";
import { mockApi } from "../../test/api";
import { renderWithApp } from "../../test/render";
import { NetworkPage } from "./NetworkPage";

// jsdom has no canvas for uPlot; the page's job is which charts it asks for.
vi.mock("../../components/charts/TimeSeriesChart", () => ({
  TimeSeriesChart: ({ title }: { title: string }) => <figure aria-label={title} />,
}));

// The default range is the six hours up to now.
const range = "from=2026-11-12T06%3A00%3A00.000Z&to=2026-11-12T12%3A00%3A00.000Z";

const network: UnifiNetwork = {
  hostId: "h1",
  hostName: "proxus",
  checkedAt: "2026-11-12T11:59:50Z",
  site: "Home",
  version: "9.4.19",
  problem: null,
  now: {
    time: "2026-11-12T11:59:50Z",
    wanRxBytesPerSec: 2 * 1024 ** 2,
    wanTxBytesPerSec: 512 * 1024,
    wiredClients: 12,
    wirelessClients: 50,
    guestClients: 0,
  },
  devices: [
    {
      mac: "aa:00",
      name: "Garage AP",
      model: "U6LR",
      type: "uap",
      online: false,
      uptimeSeconds: null,
      cpuPercent: null,
      memoryPercent: null,
      rxBytesPerSec: null,
      txBytesPerSec: null,
      clients: 0,
      seenAt: "2026-11-12T11:59:50Z",
      stateSince: "2026-11-12T11:00:00Z",
    },
    {
      mac: "aa:01",
      name: "Cloud Gateway",
      model: "UCGULTRA",
      type: "udm",
      online: true,
      uptimeSeconds: 86_400 * 3,
      cpuPercent: 12,
      memoryPercent: 60,
      rxBytesPerSec: 1000,
      txBytesPerSec: 2000,
      clients: 62,
      seenAt: "2026-11-12T11:59:50Z",
      stateSince: "2026-11-01T00:00:00Z",
    },
  ],
};

const history: MetricSeries = {
  from: "2026-11-12T06:00:00Z",
  to: "2026-11-12T12:00:00Z",
  resolution: "raw",
  bucketSeconds: 60,
  time: [1, 2],
  series: { wanRx: [1, 2], wanTx: [1, 2], wired: [1, 1], wireless: [2, 2], guests: [0, 0] },
};

const clients: UnifiClientTotal[] = [
  {
    mac: "11:22",
    name: "Living room TV",
    ip: "10.0.10.20",
    wired: false,
    network: "Home",
    downloadedBytes: 5 * 1024 ** 3,
    uploadedBytes: 1024 ** 2,
    lastSeen: "2026-11-12T11:59:00Z",
  },
];

describe("NetworkPage", () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ["Date"], now: Date.parse("2026-11-12T12:00:00Z") });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("shows internet traffic, devices and what each client used", async () => {
    mockApi({
      "GET /api/unifi": [network],
      [`GET /api/unifi/h1/history?${range}&points=300`]: history,
      [`GET /api/unifi/h1/clients?${range}`]: clients,
    });
    renderWithApp(<NetworkPage />);

    expect(await screen.findByText("2.0 MB/s")).toBeInTheDocument();
    expect(screen.getByText("12 wired, 50 wireless")).toBeInTheDocument();
    expect(screen.getByText("1 of 2")).toBeInTheDocument();
    expect(await screen.findByRole("figure", { name: "Internet" })).toBeInTheDocument();
    expect(screen.getByRole("figure", { name: "Clients" })).toBeInTheDocument();

    const garage = screen.getByText("Garage AP").closest("tr")!;
    expect(within(garage).getByText("Offline")).toBeInTheDocument();
    expect(within(garage).getByText("Access point")).toBeInTheDocument();

    const tv = (await screen.findByText("Living room TV")).closest("tr")!;
    expect(within(tv).getByText("5.0 GB")).toBeInTheDocument();
    expect(within(tv).getByText("Home")).toBeInTheDocument();
  });

  it("says what went wrong reading the console", async () => {
    mockApi({
      "GET /api/unifi": [{ ...network, problem: "The UniFi console refused the agent's API key." }],
      [`GET /api/unifi/h1/history?${range}&points=300`]: history,
      [`GET /api/unifi/h1/clients?${range}`]: [],
    });
    renderWithApp(<NetworkPage />);

    expect(await screen.findByText("The UniFi console refused the agent's API key.")).toBeInTheDocument();
  });

  it("explains how to add a network", async () => {
    mockApi({ "GET /api/unifi": [] });
    renderWithApp(<NetworkPage />);

    expect(await screen.findByText("No UniFi network yet")).toBeInTheDocument();
  });
});
