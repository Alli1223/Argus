import { screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { browserTimeZone } from "../../api/summary";
import type { UsageSummary } from "../../api/types";
import { stats } from "../../test/usage";
import { mockApi } from "../../test/api";
import { renderWithApp } from "../../test/render";
import { SummaryPage } from "./SummaryPage";

const path = (period: string, count: number) =>
  `GET /api/summary?period=${period}&count=${count}&timeZone=${encodeURIComponent(browserTimeZone())}`;

const GB = 1024 ** 3;

const days: UsageSummary = {
  period: "Day",
  timeZone: "UTC",
  historyDays: 730,
  hosts: [
    { hostId: "h1", displayName: "nas" },
    { hostId: "h2", displayName: "web" },
  ],
  periods: [
    {
      start: "2026-11-11T00:00:00Z",
      end: "2026-11-12T00:00:00Z",
      totals: stats({
        networkReceivedBytes: 3 * GB,
        diskWriteBytes: 2 * GB,
        reportingSeconds: 100,
        alerts: 2,
        criticalAlerts: 1,
      }),
      hosts: [
        { hostId: "h1", stats: stats({ networkReceivedBytes: 1 * GB, reportingSeconds: 50 }) },
        { hostId: "h2", stats: stats({ networkReceivedBytes: 2 * GB, reportingSeconds: 50 }) },
      ],
    },
    {
      start: "2026-11-10T00:00:00Z",
      end: "2026-11-11T00:00:00Z",
      totals: stats({ networkReceivedBytes: 5 * GB, reportingSeconds: 100 }),
      hosts: [{ hostId: "h1", stats: stats({ networkReceivedBytes: 5 * GB, reportingSeconds: 100 }) }],
    },
  ],
};

describe("SummaryPage", () => {
  it("shows today's totals, the fleet by day and each machine", async () => {
    mockApi({ [path("Day", 30)]: days });
    renderWithApp(<SummaryPage />);

    expect(await screen.findByRole("group", { name: /^Network\./ })).toBeInTheDocument();
    expect(screen.getAllByText("3.0 GB").length).toBeGreaterThan(0);
    expect(screen.getByText("Yesterday: 5.0 GB")).toBeInTheDocument();
    expect(screen.getAllByText("1 critical").length).toBeGreaterThan(0);

    const machines = screen.getAllByRole("table")[1];
    const rows = within(machines).getAllByRole("row").slice(1);
    // Most downloaded first.
    expect(rows.map((row) => within(row).getByRole("link").textContent)).toEqual(["web", "nas"]);
  });

  it("chooses a day from a bar", async () => {
    mockApi({ [path("Day", 30)]: days });
    const { user } = renderWithApp(<SummaryPage />);

    const network = await screen.findByRole("group", { name: /^Network\./ });
    network.focus();
    await user.keyboard("{ArrowLeft}{Enter}");

    expect(screen.getByRole("button", { name: "Yesterday" }).closest("tr")).toHaveAttribute(
      "aria-selected",
      "true",
    );
  });

  it("moves the machines to the day chosen in the fleet table", async () => {
    mockApi({ [path("Day", 30)]: days });
    const { user } = renderWithApp(<SummaryPage />);

    await user.click(await screen.findByRole("button", { name: "Yesterday" }));

    const machines = screen.getAllByRole("table")[1];
    const nas = within(machines).getByRole("link", { name: "nas" }).closest("tr")!;
    expect(within(nas).getByText("5.0 GB")).toBeInTheDocument();
    const web = within(machines).getByRole("link", { name: "web" }).closest("tr")!;
    expect(within(web).queryByText(/GB/)).not.toBeInTheDocument();
  });

  it("asks for months when grouped by month", async () => {
    const calls = mockApi({
      [path("Day", 30)]: days,
      [path("Month", 12)]: { ...days, period: "Month" },
    });
    const { user } = renderWithApp(<SummaryPage />);

    await user.click(await screen.findByText("Monthly"));

    expect(await screen.findByRole("button", { name: "This month" })).toBeInTheDocument();
    expect(calls.map((call) => call.path)).toContain(path("Month", 12).slice(4));
  });

  it("explains an empty fleet", async () => {
    mockApi({
      [path("Day", 30)]: { ...days, hosts: [], periods: days.periods.map((p) => ({ ...p, hosts: [] })) },
    });
    renderWithApp(<SummaryPage />);

    expect(await screen.findByText("No machines yet")).toBeInTheDocument();
  });
});
