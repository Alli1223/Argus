import { describe, expect, it } from "vitest";
import type { UnifiClientTotal } from "../../api/types";
import { clientMatches, deviceKind } from "./network";

const client: UnifiClientTotal = {
  mac: "11:22:33:44:55:66",
  name: "Living room TV",
  ip: "10.0.10.20",
  wired: false,
  network: "Home",
  downloadedBytes: 1,
  uploadedBytes: 1,
  lastSeen: "2026-11-12T10:00:00Z",
};

describe("deviceKind", () => {
  it("names UniFi's device types", () => {
    expect(deviceKind("uap")).toBe("Access point");
    expect(deviceKind("UDM")).toBe("Gateway");
    expect(deviceKind(null)).toBe("UniFi device");
  });
});

describe("clientMatches", () => {
  it("finds clients by name, address or MAC", () => {
    expect(clientMatches(client, "tv")).toBe(true);
    expect(clientMatches(client, "10.0.10.2")).toBe(true);
    expect(clientMatches(client, "44:55")).toBe(true);
    expect(clientMatches(client, "laptop")).toBe(false);
    expect(clientMatches(client, "  ")).toBe(true);
  });
});
