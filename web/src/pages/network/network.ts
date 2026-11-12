import type { UnifiClientTotal } from "../../api/types";

const KINDS: Record<string, string> = {
  ugw: "Gateway",
  udm: "Gateway",
  uxg: "Gateway",
  usw: "Switch",
  uap: "Access point",
};

/** What kind of device UniFi's type code means: "uap" is an access point. */
export function deviceKind(type: string | null): string {
  return (type && KINDS[type.toLowerCase()]) ?? "UniFi device";
}

/** Whether a client's name, IP address or MAC address contains the search, ignoring case. */
export function clientMatches(client: UnifiClientTotal, search: string): boolean {
  const text = search.trim().toLowerCase();
  if (!text) return true;
  return [client.name, client.ip ?? "", client.mac].some((value) => value.toLowerCase().includes(text));
}
