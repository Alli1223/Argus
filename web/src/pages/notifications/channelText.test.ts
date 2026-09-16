import { describe, expect, it } from "vitest";
import type { DeliverySummary, NotificationChannel } from "../../api/types";
import {
  channelToRequest,
  describeDelivery,
  describeFilter,
  describeTarget,
  splitAddresses,
  targetError,
} from "./channelText";

const channel: NotificationChannel = {
  id: "c1",
  name: "Ops",
  kind: "Slack",
  target: "https://hooks.slack.com/services/T0/B0/secret",
  minimumSeverity: "Warning",
  notifyOnResolved: true,
  enabled: true,
  lastDelivery: null,
  createdAt: "2026-09-16T10:00:00Z",
  updatedAt: "2026-09-16T10:00:00Z",
};

const now = Date.parse("2026-09-16T12:00:00Z");

const delivery = (overrides: Partial<DeliverySummary>): DeliverySummary => ({
  status: "Pending",
  attempts: 0,
  createdAt: "2026-09-16T11:55:00Z",
  sentAt: null,
  lastError: null,
  ...overrides,
});

describe("targets", () => {
  it("are checked against the kind of channel", () => {
    expect(targetError("Email", " ops@example.com; oncall@example.com ")).toBeNull();
    expect(targetError("Email", " , ")).toBe("Enter at least one email address.");
    expect(targetError("Email", "ops@example.com, ops")).toBe("'ops' is not an email address.");
    expect(targetError("Email", Array.from({ length: 11 }, (_, i) => `o${i}@example.com`).join(","))).toBe(
      "A channel can email up to 10 addresses.",
    );
    expect(targetError("Discord", "https://discord.com/api/webhooks/1/abc")).toBeNull();
    expect(targetError("Webhook", "ftp://example.com/hook")).not.toBeNull();
    expect(targetError("Slack", "hooks.slack.com/services/x")).not.toBeNull();
    expect(splitAddresses("a@x.com,b@y.com\nc@z.com")).toEqual(["a@x.com", "b@y.com", "c@z.com"]);
  });

  it("hide webhook tokens when shown", () => {
    expect(describeTarget(channel)).toBe("hooks.slack.com");
    expect(describeTarget({ kind: "Email", target: "ops@example.com" })).toBe("ops@example.com");
  });
});

describe("channel summaries", () => {
  it("say which alerts a channel passes on", () => {
    expect(describeFilter(channel)).toBe("Warning and critical alerts, and when they resolve");
    expect(describeFilter({ minimumSeverity: "Info", notifyOnResolved: false })).toBe(
      "All alerts, when they fire",
    );
  });

  it("say how the latest notification went", () => {
    expect(describeDelivery(null, now)).toEqual({ text: "Nothing sent yet", tone: "none" });
    expect(describeDelivery(delivery({ status: "Sent", sentAt: "2026-09-16T11:58:00Z" }), now)).toEqual({
      text: "Sent 2m ago",
      tone: "sent",
    });
    expect(describeDelivery(delivery({ attempts: 1 }), now).tone).toBe("waiting");
    expect(describeDelivery(delivery({ attempts: 2, lastError: "Connection refused" }), now)).toEqual({
      text: "Trying again after 2 attempts: Connection refused",
      tone: "problem",
    });
    expect(
      describeDelivery(delivery({ status: "Failed", attempts: 6, lastError: "Timed out" }), now).text,
    ).toBe("Not delivered: Timed out");
  });

  it("turn back into the request that saves them", () => {
    expect(channelToRequest(channel)).toEqual({
      name: "Ops",
      kind: "Slack",
      target: "https://hooks.slack.com/services/T0/B0/secret",
      minimumSeverity: "Warning",
      notifyOnResolved: true,
      enabled: true,
    });
  });
});
