import { screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { ServerSelfUpdate, ServerUpdateInfo, ServerUpdateRun } from "../../api/types";
import { mockApi } from "../../test/api";
import { renderWithApp } from "../../test/render";
import { ServerUpdatesSection } from "./ServerUpdatesSection";

const info: ServerUpdateInfo = {
  currentVersion: "0.3.0",
  enabled: true,
  latest: {
    version: "0.4.0",
    tag: "v0.4.0",
    name: "Argus 0.4.0",
    notes: "- Updates from the web app",
    url: "https://github.com/Alli1223/Argus/releases/tag/v0.4.0",
    publishedAt: "2026-09-17T10:00:00Z",
  },
  updateAvailable: true,
  checkedAt: "2026-09-17T11:59:00Z",
  error: null,
};

const ready: ServerSelfUpdate = {
  available: true,
  unavailable: null,
  updaterVersion: "0.4.0",
  pendingVersion: null,
  lastRun: null,
};

const run = (overrides: Partial<ServerUpdateRun>): ServerUpdateRun => ({
  id: "run-1",
  from: "0.3.0",
  to: "0.4.0",
  requestedBy: "admin@example.com",
  state: "backing-up",
  inProgress: true,
  startedAt: "2026-09-17T12:00:00Z",
  finishedAt: null,
  error: null,
  backup: null,
  serverLog: null,
  log: [],
  ...overrides,
});

describe("ServerUpdatesSection", () => {
  it("gives the commands to install a release when Argus cannot install it itself", async () => {
    mockApi({
      "GET /api/updates": info,
      "GET /api/updates/server": { ...ready, available: false, unavailable: "The updater is not running." },
    });
    renderWithApp(<ServerUpdatesSection />);

    expect(await screen.findByText("- Updates from the web app")).toBeInTheDocument();
    expect(
      await screen.findByText("Argus cannot install it by itself: The updater is not running."),
    ).toBeInTheDocument();
    expect(screen.getByText(/git checkout v0\.4\.0/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Update to 0.4.0" })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "The release on GitHub" })).toHaveAttribute(
      "href",
      info.latest!.url,
    );
  });

  it("says when the last check failed", async () => {
    mockApi({
      "GET /api/updates": { ...info, latest: null, updateAvailable: false, error: "GitHub answered 503." },
      "GET /api/updates/server": ready,
    });
    renderWithApp(<ServerUpdatesSection />);

    expect(await screen.findByText("This server runs Argus 0.3.0, the latest release.")).toBeInTheDocument();
    expect(screen.getByText("The last check failed: GitHub answered 503.")).toBeInTheDocument();
  });

  it("asks the updater for the release once the administrator confirms", async () => {
    const calls = mockApi({
      "GET /api/updates": info,
      "GET /api/updates/server": ready,
      "POST /api/updates/server": { ...ready, pendingVersion: "0.4.0" },
    });
    const { user } = renderWithApp(<ServerUpdatesSection />);

    await user.click(await screen.findByRole("button", { name: "Update to 0.4.0" }));
    const dialog = await screen.findByRole("dialog", { name: "Update Argus to 0.4.0?" });
    expect(within(dialog).getByText(/goes back to 0\.3\.0 by itself/)).toBeInTheDocument();
    await user.click(within(dialog).getByRole("button", { name: "Update to 0.4.0" }));

    await waitFor(() =>
      expect(calls.find((call) => call.method === "POST")?.body).toEqual({ version: "0.4.0" }),
    );
    expect(await screen.findByText("Waiting for the updater to start.")).toBeInTheDocument();
  });

  it("shows each step of an update under way", async () => {
    mockApi({ "GET /api/updates": info, "GET /api/updates/server": { ...ready, lastRun: run({}) } });
    renderWithApp(<ServerUpdatesSection />);

    const steps = await screen.findByRole("list", { name: "Update steps" });
    expect(
      within(steps)
        .getAllByRole("listitem")
        .map((item) => item.textContent),
    ).toEqual([
      "Download Argus 0.4.0(done)",
      "Back up the database(in progress)",
      "Start Argus 0.4.0(to do)",
      "Check that it keeps running(to do)",
    ]);
    expect(screen.queryByRole("button", { name: "Update to 0.4.0" })).not.toBeInTheDocument();
  });

  it("explains a rollback, with the new version's log and the backup", async () => {
    mockApi({
      "GET /api/updates": info,
      "GET /api/updates/server": {
        ...ready,
        lastRun: run({
          state: "rolled-back",
          inProgress: false,
          finishedAt: "2026-09-17T12:05:00Z",
          error: "Argus 0.4.0 did not start properly.",
          backup: "/srv/argus/deploy/backups/argus-0.3.0-before-0.4.0.dump",
          serverLog: "Unhandled exception: the database is on fire",
        }),
      },
    });
    const { user } = renderWithApp(<ServerUpdatesSection />);

    expect(
      await screen.findByText("The update to 0.4.0 did not work, so Argus went back to 0.3.0"),
    ).toBeInTheDocument();
    expect(screen.getByText("Argus 0.4.0 did not start properly.")).toBeInTheDocument();
    expect(screen.getByText(/argus-0\.3\.0-before-0\.4\.0\.dump on the server/)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Show the new version's log" }));
    expect(screen.getByText("Unhandled exception: the database is on fire")).toBeInTheDocument();
    // Going back leaves the release on offer, to try again.
    expect(screen.getByRole("button", { name: "Update to 0.4.0" })).toBeInTheDocument();
  });

  it("offers a reload when the page came from the version before the update", async () => {
    mockApi({
      "GET /api/updates": { ...info, currentVersion: "0.4.0", updateAvailable: false },
      "GET /api/updates/server": {
        ...ready,
        lastRun: run({ state: "succeeded", inProgress: false, finishedAt: "2026-09-17T12:03:00Z" }),
      },
      "GET /api/info": { name: "Argus", version: "0.3.0", publicUrl: null },
    });
    renderWithApp(<ServerUpdatesSection />);

    expect(await screen.findByText("Updated to Argus 0.4.0")).toBeInTheDocument();
    expect(await screen.findByRole("button", { name: "Reload to use 0.4.0" })).toBeInTheDocument();
  });
});
