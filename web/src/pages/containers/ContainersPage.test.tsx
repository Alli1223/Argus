import { screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { ContainerHost } from "../../api/types";
import { container } from "../../lib/containers.test";
import { mockApi } from "../../test/api";
import { renderWithApp } from "../../test/render";
import { ContainersPage } from "./ContainersPage";

const host = (overrides: Partial<ContainerHost>): ContainerHost => ({
  hostId: "h1",
  hostName: "docker-1",
  checkedAt: "2026-09-17T11:00:00Z",
  problem: null,
  actionsEnabled: false,
  containers: [],
  ...overrides,
});

describe("ContainersPage", () => {
  it("lists every host's containers with problems first, and filters to them", async () => {
    mockApi({
      "GET /api/containers": [
        host({
          containers: [
            container({
              name: "web",
              usage: {
                time: "",
                cpuPercent: 4,
                memoryBytes: 64 * 1024 ** 2,
                memoryLimitBytes: null,
                netRxBytesPerSec: 10,
                netTxBytesPerSec: 5,
              },
            }),
            container({ name: "worker", state: "restarting", restartsLastHour: 6 }),
          ],
        }),
        host({
          hostId: "h2",
          hostName: "nas",
          problem: "permission denied",
          containers: [container({ name: "backup", state: "exited", exitCode: 0 })],
        }),
      ],
    });
    const { user } = renderWithApp(<ContainersPage />);

    expect(
      await screen.findByText("2 running, 1 stopped, 1 needs attention, on 2 hosts."),
    ).toBeInTheDocument();
    expect(screen.getByText("permission denied")).toBeInTheDocument();
    const rows = () =>
      screen
        .getAllByRole("row")
        .slice(1)
        .map((row) => within(row).getAllByRole("link")[0].textContent);
    expect(rows()).toEqual(["worker", "web", "backup"]);
    expect(screen.getByText("Restarted 6 times in the last hour.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "worker" })).toHaveAttribute(
      "href",
      "/hosts/h1/containers/worker",
    );

    await user.click(screen.getByText("Need attention (1)"));
    expect(rows()).toEqual(["worker"]);
  });

  it("explains how to start watching containers when no host reports any", async () => {
    mockApi({ "GET /api/containers": [] });
    renderWithApp(<ContainersPage />);

    expect(await screen.findByText("No containers yet")).toBeInTheDocument();
    expect(screen.getByText("--docker")).toBeInTheDocument();
  });
});
