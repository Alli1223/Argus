import { screen, waitFor } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { mockApi } from "../../test/api";
import { renderWithApp } from "../../test/render";
import { ChannelEditor } from "./ChannelEditor";

const support = { email: true, reportHourUtc: 7, weeklyReportDay: "Monday" };

const saved = (body: unknown) => ({
  id: "c1",
  ...(body as object),
  lastDelivery: null,
  createdAt: "2026-09-16T12:00:00Z",
  updatedAt: "2026-09-16T12:00:00Z",
});

describe("ChannelEditor", () => {
  it("checks the webhook address before creating the channel", async () => {
    const calls = mockApi({
      "GET /api/notification-channels/support": support,
      "POST /api/notification-channels": saved,
    });
    const { user } = renderWithApp(<ChannelEditor channel={null} opened onClose={() => {}} />);

    await user.type(screen.getByLabelText(/^Name/), "Chat");
    await user.click(screen.getByRole("radio", { name: "Slack" }));
    const target = screen.getByLabelText(/^Slack webhook URL/);
    await user.type(target, "hooks.slack.com/services/x");
    await user.click(screen.getByRole("button", { name: "Create channel" }));

    expect(
      await screen.findByText("Enter the whole webhook address, starting with https://."),
    ).toBeInTheDocument();
    expect(calls.some((call) => call.method === "POST")).toBe(false);

    await user.clear(target);
    await user.type(target, "https://hooks.slack.com/services/x");
    await user.click(screen.getByRole("switch", { name: /^Weekly report/ }));
    await user.click(screen.getByRole("button", { name: "Create channel" }));

    await waitFor(() => expect(calls.some((call) => call.method === "POST")).toBe(true));
    expect(calls.find((call) => call.method === "POST")?.body).toEqual({
      name: "Chat",
      kind: "Slack",
      target: "https://hooks.slack.com/services/x",
      minimumSeverity: "Warning",
      notifyOnResolved: true,
      enabled: true,
      dailyReport: false,
      weeklyReport: true,
    });
  });

  it("tidies email addresses and says when reports go out", async () => {
    const calls = mockApi({
      "GET /api/notification-channels/support": support,
      "POST /api/notification-channels": saved,
    });
    const { user } = renderWithApp(<ChannelEditor channel={null} opened onClose={() => {}} />);

    expect(await screen.findByText(/every day at 07:00 UTC/)).toBeInTheDocument();
    expect(screen.getByText(/Mondays at 07:00 UTC/)).toBeInTheDocument();

    await user.type(screen.getByLabelText(/^Name/), "Ops");
    await user.type(screen.getByLabelText(/^Email addresses/), "ops@example.com;  oncall@example.com");
    await user.click(screen.getByRole("button", { name: "Create channel" }));

    await waitFor(() =>
      expect(calls.find((call) => call.method === "POST")?.body).toMatchObject({
        kind: "Email",
        target: "ops@example.com, oncall@example.com",
      }),
    );
  });

  it("warns when the server cannot send email", async () => {
    mockApi({ "GET /api/notification-channels/support": { ...support, email: false } });
    renderWithApp(<ChannelEditor channel={null} opened onClose={() => {}} />);

    expect(await screen.findByText("Email is not set up on this server")).toBeInTheDocument();
  });
});
