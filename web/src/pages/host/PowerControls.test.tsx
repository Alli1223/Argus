import { screen, waitFor } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { HostDetail, HostPower } from "../../api/types";
import { mockApi } from "../../test/api";
import { renderWithApp } from "../../test/render";
import { describeAsker, describeMacs, describeRelays } from "../../lib/power";
import { PowerMenu, PowerSection } from "./PowerControls";

const power: HostPower = {
  powerActionsEnabled: true,
  listening: true,
  macAddresses: ["aa:bb:cc:dd:ee:01"],
  networks: ["192.168.1.20/24"],
  wakeMacAddress: null,
  wakeAddress: null,
  wakeRelays: 1,
  history: [
    {
      id: "p1",
      kind: "Shutdown",
      requestedBy: null,
      ruleId: "r1",
      ruleName: "Power cut",
      reason: "UPS ups on nas is on battery with charge below 40%",
      succeeded: true,
      detail: null,
      at: "2026-10-15T10:00:00Z",
    },
  ],
};

const host = { id: "h1", displayName: "desktop", status: "Online" } as HostDetail;

describe("power controls", () => {
  it("describe how a machine would be woken", () => {
    expect(describeMacs(power)).toBe("aa:bb:cc:dd:ee:01");
    expect(describeMacs({ ...power, wakeMacAddress: "aa:bb:cc:dd:ee:99" })).toBe(
      "aa:bb:cc:dd:ee:99 (set by hand)",
    );
    expect(describeRelays(0)).toBe("No other agent is connected to send it");
    expect(describeRelays(2)).toBe("2 of your other machines' agents");
    expect(describeAsker(power.history[0])).toBe("Rule: Power cut");
    expect(describeAsker({ ...power.history[0], requestedBy: "me@example.com" })).toBe("me@example.com");
  });

  it("shut a machine down after asking", async () => {
    const calls = mockApi({
      "GET /api/hosts/h1/power": power,
      "POST /api/hosts/h1/power/shutdown": { ...power.history[0], id: "p2", requestedBy: "me@example.com" },
    });
    const { user } = renderWithApp(<PowerMenu host={host} />);

    await user.click(screen.getByRole("button", { name: "Power" }));
    await user.click(await screen.findByRole("menuitem", { name: "Shut down…" }));
    await user.click(await screen.findByRole("button", { name: "Shut down" }));

    await waitFor(() => expect(calls.some((call) => call.method === "POST")).toBe(true));
    expect(calls.find((call) => call.method === "POST")?.path).toBe("/api/hosts/h1/power/shutdown");
  });

  it("only offer shutting down where the agent allows it", async () => {
    mockApi({ "GET /api/hosts/h1/power": { ...power, powerActionsEnabled: false } });
    const { user } = renderWithApp(<PowerMenu host={host} />);

    await user.click(screen.getByRole("button", { name: "Power" }));
    await waitFor(() => expect(screen.getByRole("menuitem", { name: "Shut down…" })).toBeDisabled());
    expect(screen.getByRole("menuitem", { name: "Wake with Wake-on-LAN" })).toBeEnabled();
    expect(screen.getByText(/--power-actions/)).toBeInTheDocument();
  });

  it("list what was done, and by which rule", async () => {
    mockApi({ "GET /api/hosts/h1/power": power });
    renderWithApp(<PowerSection hostId="h1" now={Date.parse("2026-10-15T10:05:00Z")} />);

    expect(await screen.findByText("Rule: Power cut")).toBeInTheDocument();
    expect(screen.getByText("UPS ups on nas is on battery with charge below 40%")).toBeInTheDocument();
    expect(screen.getByText("1 of your other machines' agents")).toBeInTheDocument();
  });
});
