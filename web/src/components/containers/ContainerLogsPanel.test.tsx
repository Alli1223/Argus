import { screen, waitFor } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { ContainerSummary } from "../../api/types";
import { container } from "../../lib/containers.test";
import { mockApi, reply } from "../../test/api";
import { renderWithApp } from "../../test/render";
import { ContainerActions } from "./ContainerActions";
import { ContainerLogsPanel } from "./ContainerLogsPanel";

describe("ContainerLogsPanel", () => {
  it("shows the newest lines, marking stderr", async () => {
    mockApi({
      "GET /api/hosts/h1/containers/web/logs?tail=100": {
        lines: [
          { time: "2026-09-17T11:00:01Z", stream: "stdout", text: "listening on :80" },
          { time: "2026-09-17T11:00:02Z", stream: "stderr", text: "warning: no config" },
        ],
        truncated: true,
      },
    });
    renderWithApp(<ContainerLogsPanel hostId="h1" name="web" actionsEnabled />);

    const log = await screen.findByRole("log", { name: "web logs" });
    await waitFor(() => expect(log).toHaveTextContent("listening on :80"));
    expect(log).toHaveTextContent(/err warning: no config/);
    expect(screen.getByText("Older lines are left out.")).toBeInTheDocument();
  });

  it("says why the logs did not come", async () => {
    mockApi({
      "GET /api/hosts/h1/containers/web/logs?tail=100": reply(409, {
        title: "The agent is not listening",
        detail: "The machine's agent is not connected for container actions right now.",
      }),
    });
    renderWithApp(<ContainerLogsPanel hostId="h1" name="web" actionsEnabled />);

    expect(await screen.findByText("The agent is not listening")).toBeInTheDocument();
  });

  it("does not ask for logs on machines that do not allow them", () => {
    const calls = mockApi({});
    renderWithApp(<ContainerLogsPanel hostId="h1" name="web" actionsEnabled={false} />);

    expect(screen.getByText(/Logs are off on this machine/)).toBeInTheDocument();
    expect(calls).toHaveLength(0);
  });
});

describe("ContainerActions", () => {
  const render = (overrides: Partial<ContainerSummary>, actionsEnabled = true) =>
    renderWithApp(
      <ContainerActions
        hostId="h1"
        hostName="docker-1"
        container={container({ name: "web", ...overrides })}
        actionsEnabled={actionsEnabled}
      />,
    );

  it("stops a running container once confirmed", async () => {
    const calls = mockApi({ "POST /api/hosts/h1/containers/web/stop": reply(204) });
    const { user } = render({ state: "running" });

    expect(screen.queryByRole("button", { name: "Start" })).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Stop" }));
    await user.click(await screen.findByRole("button", { name: "Stop container" }));

    await waitFor(() =>
      expect(calls.map((call) => `${call.method} ${call.path}`)).toContain(
        "POST /api/hosts/h1/containers/web/stop",
      ),
    );
  });

  it("starts a stopped container straight away", async () => {
    const calls = mockApi({ "POST /api/hosts/h1/containers/web/start": reply(204) });
    const { user } = render({ state: "exited", exitCode: 0 });

    await user.click(screen.getByRole("button", { name: "Start" }));

    await waitFor(() =>
      expect(calls.map((call) => call.path)).toContain("/api/hosts/h1/containers/web/start"),
    );
  });

  it("explains how to allow actions on machines that do not", () => {
    render({ state: "running" }, false);

    expect(screen.queryByRole("button", { name: /^(Start|Stop|Restart)$/ })).not.toBeInTheDocument();
    expect(screen.getByText(/--container-actions/)).toBeInTheDocument();
  });
});
