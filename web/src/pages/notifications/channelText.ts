import type {
  DeliverySummary,
  NotificationChannel,
  NotificationChannelKind,
  NotificationChannelRequest,
} from "../../api/types";
import { formatAgo } from "../../lib/format";

export const MAX_ADDRESSES = 10;

export const KIND_LABELS: Record<NotificationChannelKind, string> = {
  Email: "Email",
  Slack: "Slack",
  Discord: "Discord",
  Webhook: "Webhook",
};

/** How the target field reads for each kind of channel. */
export const TARGET_FIELDS: Record<
  NotificationChannelKind,
  { label: string; placeholder: string; description: string }
> = {
  Email: {
    label: "Email addresses",
    placeholder: "ops@example.com, oncall@example.com",
    description: `Separate addresses with commas, up to ${MAX_ADDRESSES}.`,
  },
  Slack: {
    label: "Slack webhook URL",
    placeholder: "https://hooks.slack.com/services/…",
    description: "Add an incoming webhook to your Slack workspace, then paste its address.",
  },
  Discord: {
    label: "Discord webhook URL",
    placeholder: "https://discord.com/api/webhooks/…",
    description: "In the Discord channel's settings, under Integrations, create a webhook and copy its URL.",
  },
  Webhook: {
    label: "Webhook URL",
    placeholder: "https://example.com/argus",
    description: "Argus posts each alert to this address as JSON.",
  },
};

export function splitAddresses(target: string): string[] {
  return target.split(/[\s,;]+/).filter((address) => address !== "");
}

const WEBHOOK_ERROR = "Enter the whole webhook address, starting with https://.";

/** Why a target does not suit the kind of channel, or null when it does. The server checks the same things. */
export function targetError(kind: NotificationChannelKind, target: string): string | null {
  const value = target.trim();
  if (kind === "Email") {
    const addresses = splitAddresses(value);
    if (addresses.length === 0) return "Enter at least one email address.";
    if (addresses.length > MAX_ADDRESSES) return `A channel can email up to ${MAX_ADDRESSES} addresses.`;
    const invalid = addresses.find((address) => !/^[^\s@]+@[^\s@]+$/.test(address));
    return invalid ? `'${invalid}' is not an email address.` : null;
  }
  try {
    const url = new URL(value);
    return url.protocol === "https:" || url.protocol === "http:" ? null : WEBHOOK_ERROR;
  } catch {
    return WEBHOOK_ERROR;
  }
}

/** Where a channel sends, without secrets: webhook addresses carry tokens, so only their host is shown. */
export function describeTarget(channel: Pick<NotificationChannel, "kind" | "target">): string {
  if (channel.kind === "Email") return channel.target;
  try {
    return new URL(channel.target).host;
  } catch {
    return channel.target;
  }
}

/** Which alerts a channel passes on: "Warning and critical alerts, and when they resolve". */
export function describeFilter(
  channel: Pick<NotificationChannel, "minimumSeverity" | "notifyOnResolved">,
): string {
  const which = {
    Info: "All alerts",
    Warning: "Warning and critical alerts",
    Critical: "Critical alerts",
  }[channel.minimumSeverity];
  return channel.notifyOnResolved ? `${which}, and when they resolve` : `${which}, when they fire`;
}

export type DeliveryTone = "sent" | "problem" | "waiting" | "none";

/** How a channel's latest notification went, in words. */
export function describeDelivery(
  delivery: DeliverySummary | null,
  nowMs: number,
): { text: string; tone: DeliveryTone } {
  if (!delivery) return { text: "Nothing sent yet", tone: "none" };
  switch (delivery.status) {
    case "Sent":
      return { text: `Sent ${formatAgo(delivery.sentAt ?? delivery.createdAt, nowMs)}`, tone: "sent" };
    case "Failed":
      return { text: `Not delivered: ${delivery.lastError ?? "no reason given"}`, tone: "problem" };
    default: {
      if (!delivery.lastError) return { text: "Sending", tone: "waiting" };
      const attempts = delivery.attempts === 1 ? "1 attempt" : `${delivery.attempts} attempts`;
      return { text: `Trying again after ${attempts}: ${delivery.lastError}`, tone: "problem" };
    }
  }
}

/** The request that saves a channel as it stands, for example to switch it on or off. */
export function channelToRequest(channel: NotificationChannel): NotificationChannelRequest {
  const { name, kind, target, minimumSeverity, notifyOnResolved, enabled } = channel;
  return { name, kind, target, minimumSeverity, notifyOnResolved, enabled };
}
