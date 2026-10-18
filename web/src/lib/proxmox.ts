import type { GuestAction } from "../api/proxmox";
import type { ProxmoxGuest } from "../api/types";

export const GUEST_KINDS: Record<ProxmoxGuest["type"], string> = { qemu: "VM", lxc: "Container" };

export const STATUS_LABELS: Record<ProxmoxGuest["status"], string> = {
  running: "Running",
  stopped: "Stopped",
  paused: "Paused",
  unknown: "Unknown",
};

/** What can be done to a guest now: nothing to templates or locked guests, else what suits its status. */
export function guestActions(guest: ProxmoxGuest): GuestAction[] {
  if (guest.template || guest.lock) return [];
  switch (guest.status) {
    case "running":
      return ["shutdown", "reboot", "stop"];
    case "paused":
      return ["stop"];
    case "stopped":
      return ["start"];
    default:
      return [];
  }
}

/** "3 running, 2 stopped" for a host's guests, templates left out. */
export function describeGuests(guests: ProxmoxGuest[]): string {
  const counts = new Map<string, number>();
  for (const guest of guests) {
    if (guest.template) continue;
    counts.set(guest.status, (counts.get(guest.status) ?? 0) + 1);
  }
  const order: ProxmoxGuest["status"][] = ["running", "paused", "stopped", "unknown"];
  const parts = order
    .filter((status) => counts.has(status))
    .map((status) => `${counts.get(status)} ${status}`);
  return parts.length > 0 ? parts.join(", ") : "No guests";
}

/** Uptime now, from the uptime reported and the time since the report. */
export function currentUptime(guest: ProxmoxGuest, checkedAt: string, now: number): number {
  if (guest.status !== "running") return 0;
  return guest.uptimeSeconds + Math.max(0, (now - Date.parse(checkedAt)) / 1000);
}
