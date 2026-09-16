import { screen, waitFor } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { HostAgentUpdate, HostDetail } from "../api/types";
import { mockApi, reply } from "../test/api";
import { renderWithApp } from "../test/render";
import { AgentVersion } from "./AgentVersion";

const now = Date.parse("2026-09-16T12:00:00Z");

const host = (agentUpdate: HostAgentUpdate | null) =>
  ({ id: "h1", agentVersion: "0.2.0", agentUpdate }) as HostDetail;

const update = (overrides: Partial<HostAgentUpdate>): HostAgentUpdate => ({
  available: "0.3.0",
  requested: null,
  requestedAt: null,
  error: null,
  ...overrides,
});

describe("AgentVersion", () => {
  it("asks the agent to update", async () => {
    const calls = mockApi({ "POST /api/hosts/h1/agent-update": host(update({ requested: "0.3.0" })) });
    const { user } = renderWithApp(<AgentVersion host={host(update({}))} now={now} />);

    await user.click(screen.getByRole("button", { name: "Update to 0.3.0" }));

    await waitFor(() =>
      expect(calls.map((call) => `${call.method} ${call.path}`)).toContain("POST /api/hosts/h1/agent-update"),
    );
  });

  it("says when an update seems stuck", () => {
    renderWithApp(
      <AgentVersion
        host={host(update({ requested: "0.3.0", requestedAt: "2026-09-16T11:00:00Z" }))}
        now={now}
      />,
    );

    expect(screen.getByText("Updating to 0.3.0")).toBeInTheDocument();
    expect(screen.getByText(/has not installed it yet/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Cancel" })).toBeInTheDocument();
  });

  it("shows why the last update failed", async () => {
    const calls = mockApi({
      "DELETE /api/hosts/h1/agent-update": reply(204),
      "GET /api/hosts/h1": host(update({})),
    });
    const { user } = renderWithApp(
      <AgentVersion host={host(update({ error: "Access denied" }))} now={now} />,
    );

    expect(screen.getByText("The last update failed: Access denied")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Try again" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Dismiss" }));
    await waitFor(() => expect(calls.some((call) => call.method === "DELETE")).toBe(true));
  });
});
