import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { NotificationChannel } from "../../api/types";
import { mockApi, reply } from "../../test/api";
import { renderWithApp } from "../../test/render";
import { NotificationsPage } from "./NotificationsPage";

const support = { email: true, reportHourUtc: 7, weeklyReportDay: "Monday" };

const chat: NotificationChannel = {
  id: "c1",
  name: "Chat",
  kind: "Slack",
  target: "https://hooks.slack.com/services/T0/B0/secret-token",
  minimumSeverity: "Critical",
  notifyOnResolved: false,
  enabled: true,
  dailyReport: true,
  weeklyReport: false,
  lastDelivery: {
    status: "Failed",
    attempts: 6,
    createdAt: "2026-09-16T08:00:00Z",
    sentAt: null,
    lastError: "The webhook answered 410 Gone.",
  },
  createdAt: "2026-09-15T08:00:00Z",
  updatedAt: "2026-09-15T08:00:00Z",
};

describe("NotificationsPage", () => {
  it("lists channels without their webhook tokens", async () => {
    mockApi({
      "GET /api/notification-channels": [chat],
      "GET /api/notification-channels/support": support,
    });
    renderWithApp(<NotificationsPage />);

    expect(await screen.findByText("Chat")).toBeInTheDocument();
    expect(screen.getByText("hooks.slack.com")).toBeInTheDocument();
    expect(screen.queryByText(/secret-token/)).not.toBeInTheDocument();
    expect(screen.getByText("Critical alerts, when they fire")).toBeInTheDocument();
    expect(screen.getByText("Daily report")).toBeInTheDocument();
    expect(screen.getByText("Not delivered: The webhook answered 410 Gone.")).toBeInTheDocument();
  });

  it("says why a test did not arrive", async () => {
    const calls = mockApi({
      "GET /api/notification-channels": [chat],
      "GET /api/notification-channels/support": support,
      "POST /api/notification-channels/c1/test": reply(502, {
        title: "The test was not delivered",
        detail: "The webhook answered 404 Not Found.",
      }),
    });
    const { user } = renderWithApp(<NotificationsPage />);

    await user.click(await screen.findByRole("button", { name: "Send a test to Chat" }));

    expect(await screen.findByText("The webhook answered 404 Not Found.")).toBeInTheDocument();
    expect(screen.getByText("The test did not arrive")).toBeInTheDocument();
    expect(calls.filter((call) => call.method === "POST")).toHaveLength(1);
  });

  it("invites people to add a channel when there are none", async () => {
    mockApi({
      "GET /api/notification-channels": [],
      "GET /api/notification-channels/support": support,
    });
    renderWithApp(<NotificationsPage />);

    expect(await screen.findByText("No channels, so alerts only show up in Argus.")).toBeInTheDocument();
  });
});
