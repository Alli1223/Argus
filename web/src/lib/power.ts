import type { HostPower, PowerActionInfo } from "../api/types";

export const ACTION_LABELS: Record<PowerActionInfo["kind"], string> = {
  Shutdown: "Shut down",
  Restart: "Restart",
  Wake: "Wake",
};

/** The hardware addresses a wake would name. */
export function describeMacs(power: HostPower): string {
  if (power.wakeMacAddress) return `${power.wakeMacAddress} (set by hand)`;
  if (power.macAddresses.length > 0) return power.macAddresses.join(", ");
  return "Not known yet: its agent reports it from 0.11, or set it by hand";
}

/** Which agents would send the packet. */
export function describeRelays(relays: number): string {
  if (relays === 0) return "No other agent is connected to send it";
  return relays === 1 ? "1 of your other machines' agents" : `${relays} of your other machines' agents`;
}

/** Who asked: a person, or the alert rule whose alert did. */
export function describeAsker(action: PowerActionInfo): string {
  if (action.requestedBy) return action.requestedBy;
  if (action.ruleName) return `Rule: ${action.ruleName}`;
  return action.ruleId ? "An alert rule" : "A deleted alert rule";
}
