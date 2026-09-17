import type { EnrollmentTokenSummary } from "../../api/types";

/** Where agents reach the server: the configured public address, else the address this page came from. */
export function serverAddress(publicUrl: string | null | undefined, origin: string): string {
  return (publicUrl || origin).replace(/\/+$/, "");
}

export interface InstallCommands {
  linux: string;
  windows: string;
}

/**
 * One-line installs: fetch the agent from this server, register it with the token, run it as a service.
 * With `docker`, the Linux agent may also watch Docker's containers.
 */
export function installCommands(server: string, token: string, { docker = false } = {}): InstallCommands {
  return {
    linux:
      `curl -fsSL ${server}/downloads/install.sh | sudo bash -s -- --server ${server} --token ${token}` +
      (docker ? " --docker" : ""),
    windows:
      `& ([scriptblock]::Create((Invoke-RestMethod ${server}/downloads/install.ps1)))` +
      ` -Server ${server} -Token ${token}`,
  };
}

const LOCAL_HOSTS = new Set(["localhost", "127.0.0.1", "[::1]"]);

/** True when agents would send their keys over plain HTTP to another machine. */
export function isInsecureAddress(server: string): boolean {
  try {
    const url = new URL(server);
    return url.protocol === "http:" && !LOCAL_HOSTS.has(url.hostname);
  } catch {
    return false;
  }
}

export type TokenStatus = "Active" | "Revoked" | "Expired" | "Used up";

/** Whether a token still registers machines, and if not, why. */
export function tokenStatus(token: EnrollmentTokenSummary, nowMs: number): TokenStatus {
  if (token.revokedAt) return "Revoked";
  if (token.expiresAt && Date.parse(token.expiresAt) <= nowMs) return "Expired";
  if (token.maxUses != null && token.useCount >= token.maxUses) return "Used up";
  return "Active";
}

/** "Never", "Expired", or how long is left: "In 45m", "In 5h", "In 3d". */
export function describeExpiry(expiresAt: string | null, nowMs: number): string {
  if (!expiresAt) return "Never";
  const seconds = Math.round((Date.parse(expiresAt) - nowMs) / 1000);
  if (seconds <= 0) return "Expired";
  if (seconds < 3_600) return `In ${Math.max(1, Math.round(seconds / 60))}m`;
  if (seconds < 172_800) return `In ${Math.round(seconds / 3_600)}h`;
  return `In ${Math.round(seconds / 86_400)}d`;
}
