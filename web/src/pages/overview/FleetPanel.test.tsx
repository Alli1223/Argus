import { screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { FleetMetrics, HostSummary } from "../../api/types";
import { mockApi } from "../../test/api";
import { renderWithApp } from "../../test/render";
import { FleetPanel } from "./FleetPanel";

const host = (name: string, cpu: number, memory: number, rx: number, tx: number): HostSummary =>
  ({
    id: name,
    displayName: name,
    status: "Online",
    latest: { cpuPercent: cpu, memoryPercent: memory, netRxBytesPerSec: rx, netTxBytesPerSec: tx },
  }) as HostSummary;

const FLEET: FleetMetrics = {
  totals: {
    from: "2026-10-05T10:00:00Z",
    to: "2026-10-05T11:00:00Z",
    resolution: "raw",
    bucketSeconds: 60,
    time: [1_759_658_400, 1_759_658_460],
    series: { cpu: [30, 50], netRx: [2_048, 8_192], netTx: [1_024, 1_024] },
  },
  hosts: [{ hostId: "a", displayName: "a", cpu: [20, 40] }],
};

// The default range is the six hours up to now.
const fleetPath =
  "GET /api/hosts/fleet/metrics?from=2026-10-05T05%3A00%3A00.000Z&to=2026-10-05T11%3A00%3A00.000Z&points=300";

describe("FleetPanel", () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ["Date"], now: Date.parse("2026-10-05T11:00:00Z") });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("shows the fleet now as dials and its history as charts", async () => {
    mockApi({ [fleetPath]: FLEET });
    renderWithApp(<FleetPanel hosts={[host("a", 20, 40, 3_072, 512), host("b", 60, 80, 1_024, 512)]} />);

    expect(screen.getByRole("img", { name: "Average CPU: 40.0 %" })).toBeInTheDocument();
    expect(screen.getByRole("img", { name: "Average memory: 60.0 %" })).toBeInTheDocument();
    expect(screen.getByRole("img", { name: "Received: 4.0 KB/s" })).toBeInTheDocument();
    expect(screen.getByRole("img", { name: "Sent: 1.0 KB/s" })).toBeInTheDocument();
    expect(screen.getAllByText("Per system, of 2 online systems")).toHaveLength(2);

    expect(await screen.findByText("CPU across all systems")).toBeInTheDocument();
    expect(screen.getByText("Network across all systems")).toBeInTheDocument();
    // Once the history is in, traffic dials run up to the busiest moment in the range.
    expect(screen.getByText("peak 8 KB/s")).toBeInTheDocument();
  });
});
