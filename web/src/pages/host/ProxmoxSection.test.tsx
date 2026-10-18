import { screen, waitFor } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { HostProxmox, ProxmoxGuest } from "../../api/types";
import { currentUptime, describeGuests, guestActions } from "../../lib/proxmox";
import { mockApi, reply } from "../../test/api";
import { renderWithApp } from "../../test/render";
import { ProxmoxSection } from "./ProxmoxSection";

const now = Date.parse("2026-10-18T12:00:00Z");

const guest = (overrides: Partial<ProxmoxGuest>): ProxmoxGuest => ({
  id: 100,
  type: "qemu",
  name: "web",
  status: "running",
  lock: null,
  template: false,
  uptimeSeconds: 3600,
  cpuPercent: 12,
  cpus: 4,
  memoryBytes: 2 * 1024 ** 3,
  maxMemoryBytes: 4 * 1024 ** 3,
  maxDiskBytes: null,
  tags: [],
  statusSince: "2026-10-18T09:00:00Z",
  ...overrides,
});

const report: HostProxmox = {
  checkedAt: "2026-10-18T11:59:30Z",
  node: "pve",
  version: "8.2.4",
  problem: null,
  actionsEnabled: true,
  listening: true,
  guests: [
    guest({}),
    guest({
      id: 101,
      type: "lxc",
      name: "dns",
      status: "stopped",
      cpuPercent: null,
      memoryBytes: null,
      uptimeSeconds: 0,
    }),
  ],
};

describe("proxmox guests", () => {
  it("offer what suits each guest's status", () => {
    expect(guestActions(guest({}))).toEqual(["shutdown", "reboot", "stop"]);
    expect(guestActions(guest({ status: "stopped" }))).toEqual(["start"]);
    expect(guestActions(guest({ status: "paused" }))).toEqual(["stop"]);
    expect(guestActions(guest({ lock: "backup" }))).toEqual([]);
    expect(guestActions(guest({ template: true, status: "stopped" }))).toEqual([]);
  });

  it("are counted and their uptime kept current", () => {
    expect(describeGuests(report.guests)).toBe("1 running, 1 stopped");
    expect(describeGuests([guest({ template: true })])).toBe("No guests");
    expect(currentUptime(guest({}), "2026-10-18T11:59:00Z", now)).toBe(3660);
  });

  it("are not shown for hosts whose agent does not read Proxmox VE", async () => {
    const calls = mockApi({ "GET /api/hosts/h1/proxmox": { ...report, checkedAt: null, guests: [] } });
    renderWithApp(<ProxmoxSection hostId="h1" now={now} />);

    await expect.poll(() => calls.length).toBe(1);
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(
      screen.queryByRole("heading", { name: "Virtual machines and containers" }),
    ).not.toBeInTheDocument();
  });

  it("are listed with their status, and shut down after asking", async () => {
    const calls = mockApi({
      "GET /api/hosts/h1/proxmox": report,
      "POST /api/hosts/h1/proxmox/guests/100/shutdown": reply(204),
    });
    const { user } = renderWithApp(<ProxmoxSection hostId="h1" now={now} />);

    expect(await screen.findByText("1 running, 1 stopped on node pve, checked 30s ago")).toBeInTheDocument();
    expect(screen.getByText("dns")).toBeInTheDocument();
    expect(screen.getByText("Container")).toBeInTheDocument();
    expect(screen.getAllByText("For 3h 0m")).toHaveLength(2);
    expect(screen.getByText("2.0 GB of 4.0 GB")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Actions for web" }));
    await user.click(await screen.findByRole("menuitem", { name: "Shut down…" }));
    await user.click(await screen.findByRole("button", { name: "Shut down" }));

    await waitFor(() => expect(calls.some((call) => call.method === "POST")).toBe(true));
    expect(calls.find((call) => call.method === "POST")?.path).toBe(
      "/api/hosts/h1/proxmox/guests/100/shutdown",
    );
  });

  it("start without asking", async () => {
    const calls = mockApi({
      "GET /api/hosts/h1/proxmox": report,
      "POST /api/hosts/h1/proxmox/guests/101/start": reply(204),
    });
    const { user } = renderWithApp(<ProxmoxSection hostId="h1" now={now} />);

    await user.click(await screen.findByRole("button", { name: "Actions for dns" }));
    await user.click(await screen.findByRole("menuitem", { name: "Start" }));

    await waitFor(() => expect(calls.some((call) => call.method === "POST")).toBe(true));
    expect(calls.find((call) => call.method === "POST")?.path).toBe("/api/hosts/h1/proxmox/guests/101/start");
  });

  it("say how to switch actions on, and why the agent cannot read Proxmox VE", async () => {
    mockApi({
      "GET /api/hosts/h1/proxmox": {
        ...report,
        actionsEnabled: false,
        problem: "Proxmox VE refused the agent's API token.",
      },
    });
    renderWithApp(<ProxmoxSection hostId="h1" now={now} />);

    expect(await screen.findByText("The agent cannot read Proxmox VE")).toBeInTheDocument();
    expect(screen.getByText(/--proxmox-actions/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Actions for web" })).not.toBeInTheDocument();
  });
});
