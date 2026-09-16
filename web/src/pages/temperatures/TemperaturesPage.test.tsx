import { screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { HostSummary, HostTemperatures, MetricSeries } from "../../api/types";
import { mockApi } from "../../test/api";
import { renderWithApp } from "../../test/render";
import { TemperaturesPage } from "./TemperaturesPage";

// jsdom has no canvas for uPlot; the page's job is which charts it asks for.
vi.mock("../../components/charts/TimeSeriesChart", () => ({
  TimeSeriesChart: ({ title, description }: { title: string; description?: string }) => (
    <figure aria-label={title}>{description}</figure>
  ),
}));

// The default range is the six hours up to now.
const fleetPath =
  "GET /api/hosts/temperatures?from=2026-09-16T06%3A00%3A00.000Z&to=2026-09-16T12%3A00%3A00.000Z&points=300";

function history(keys: string[]): MetricSeries {
  return {
    from: "2026-09-16T06:00:00Z",
    to: "2026-09-16T12:00:00Z",
    resolution: "raw",
    bucketSeconds: 60,
    time: [1, 2],
    series: Object.fromEntries(keys.map((key) => [key, [50, 51]])),
  };
}

function host(id: string, displayName: string, status: HostSummary["status"] = "Online"): HostSummary {
  return {
    id,
    displayName,
    hostname: displayName,
    platform: "Linux",
    osName: null,
    tags: [],
    status,
    lastSeenAt: null,
    agentVersion: "0.3.0",
    ownerId: "u1",
    latest: null,
    agentUpdate: null,
  };
}

describe("TemperaturesPage", () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ["Date"], now: Date.parse("2026-09-16T12:00:00Z") });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("shows each reporting host's devices and names the hosts without readings", async () => {
    const fleet: HostTemperatures[] = [
      {
        hostId: "h1",
        displayName: "nas",
        history: history(["coretemp/Package id 0", "coretemp/Core 0", "nvme0/Composite"]),
      },
    ];
    mockApi({
      [fleetPath]: fleet,
      "GET /api/hosts": [host("h1", "nas"), host("h2", "vm-1", "Offline")],
    });
    renderWithApp(<TemperaturesPage />);

    expect(await screen.findByRole("link", { name: "nas" })).toHaveAttribute("href", "/hosts/h1");
    expect(screen.getByRole("figure", { name: "coretemp" })).toHaveTextContent("Processor");
    expect(screen.getByRole("figure", { name: "nvme0" })).toHaveTextContent("NVMe drive");
    expect(await screen.findByText(/No temperatures in this range from/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "vm-1" })).toHaveAttribute("href", "/hosts/h2");
  });

  it("explains where temperatures come from when no host reports any", async () => {
    mockApi({ [fleetPath]: [], "GET /api/hosts": [host("h2", "vm-1")] });
    renderWithApp(<TemperaturesPage />);

    expect(await screen.findByText("No temperatures in this range")).toBeInTheDocument();
    expect(screen.getByText(/ACPI thermal zones on Windows/)).toBeInTheDocument();
  });
});
