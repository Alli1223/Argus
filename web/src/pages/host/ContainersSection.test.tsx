import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { container } from "../../lib/containers.test";
import { mockApi } from "../../test/api";
import { renderWithApp } from "../../test/render";
import { ContainersSection } from "./HostResources";

const now = Date.parse("2026-09-17T12:00:00Z");

describe("ContainersSection", () => {
  it("shows nothing for hosts whose agent does not watch Docker", async () => {
    const calls = mockApi({
      "GET /api/hosts/h1/containers": {
        checkedAt: null,
        engineVersion: null,
        problem: null,
        actionsEnabled: false,
        containers: [],
      },
    });
    renderWithApp(<ContainersSection hostId="h1" now={now} />);

    await expect.poll(() => calls.length).toBe(1);
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(screen.queryByRole("heading", { name: "Containers" })).not.toBeInTheDocument();
  });

  it("says when the agent cannot read Docker, keeping what it last saw", async () => {
    mockApi({
      "GET /api/hosts/h1/containers": {
        checkedAt: "2026-09-17T11:59:30Z",
        engineVersion: "29.8.1",
        problem: "The agent is not allowed to use Docker's socket.",
        actionsEnabled: false,
        containers: [container({ name: "web", stateSince: "2026-09-17T09:00:00Z" })],
      },
    });
    renderWithApp(<ContainersSection hostId="h1" now={now} />);

    expect(await screen.findByText("The agent cannot read Docker")).toBeInTheDocument();
    expect(screen.getByText(/The containers below are as it last saw them/)).toBeInTheDocument();
    expect(screen.getByText("1 running, checked 30s ago")).toBeInTheDocument();
    expect(screen.getByText("For 3h 0m")).toBeInTheDocument();
  });
});
