import { screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { HostSummary } from "../../api/types";
import { mockApi } from "../../test/api";
import { renderWithApp } from "../../test/render";
import { HostsPage } from "./HostsPage";

const host = (name: string, rx: number, tx: number, uptime: number, online = true): HostSummary => ({
  id: name,
  displayName: name,
  hostname: name,
  platform: "Linux",
  osName: "Ubuntu",
  tags: [],
  status: online ? "Online" : "Offline",
  lastSeenAt: "2026-10-05T10:00:00Z",
  agentVersion: "0.9.0",
  ownerId: "u1",
  latest: {
    time: "2026-10-05T10:00:00Z",
    cpuPercent: 10,
    memoryPercent: 20,
    memoryUsedBytes: 1,
    memoryTotalBytes: 5,
    swapPercent: null,
    load1: 0.1,
    diskUsedPercent: 30,
    netRxBytesPerSec: rx,
    netTxBytesPerSec: tx,
    uptimeSeconds: uptime,
    diskReadBytesPerSec: null,
    diskWriteBytesPerSec: null,
  },
  agentUpdate: null,
});

/** Host names in table order. */
function order(): string[] {
  return screen
    .getAllByRole("row")
    .slice(1)
    .map((row) => within(row).getAllByRole("link")[0].textContent ?? "");
}

describe("HostsPage", () => {
  it("sorts by traffic received and sent, busiest first, and by uptime", async () => {
    mockApi({
      "GET /api/hosts": [
        host("alpha", 1_000, 900_000, 60),
        host("bravo", 500_000, 2_000, 86_400),
        host("charlie", 50_000, 50_000, 3_600),
        host("delta", 9_000_000, 9_000_000, 999_999, false),
      ],
      "GET /api/auth/me": { id: "u1", email: "a@b.c", displayName: "A", roles: [], isAdmin: false },
    });
    const { user } = renderWithApp(<HostsPage />);
    await screen.findByText("alpha");

    await user.click(screen.getByRole("button", { name: "Received" }));
    expect(order()).toEqual(["delta", "bravo", "charlie", "alpha"]);

    await user.click(screen.getByRole("button", { name: "Sent" }));
    expect(order()).toEqual(["delta", "alpha", "charlie", "bravo"]);
    await user.click(screen.getByRole("button", { name: "Sent" }));
    expect(order()).toEqual(["bravo", "charlie", "alpha", "delta"]);

    // Longest first; an offline host's uptime is out of date, so it goes last.
    await user.click(screen.getByRole("button", { name: "Uptime" }));
    expect(order()).toEqual(["bravo", "charlie", "alpha", "delta"]);
  }, 15_000);
});
