import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { refreshInterval, resolveRange, type TimeRange } from "../lib/timeRange";
import { api, query } from "./client";
import type { MetricSeries, UnifiClientTotal, UnifiNetwork } from "./types";

export const unifiKeys = {
  networks: ["unifi", "networks"] as const,
  history: (hostId: string, range: TimeRange) => ["unifi", "history", hostId, range] as const,
  clients: (hostId: string, range: TimeRange) => ["unifi", "clients", hostId, range] as const,
};

const iso = (seconds: number) => new Date(seconds * 1000).toISOString();

/** The UniFi networks the user's hosts read, with their devices. Devices are reported about once a minute. */
export function useUnifiNetworks() {
  return useQuery({
    queryKey: unifiKeys.networks,
    queryFn: () => api.get<UnifiNetwork[]>("/api/unifi"),
    refetchInterval: 30_000,
  });
}

/** Internet traffic (`wanRx`, `wanTx`) and clients (`wired`, `wireless`, `guests`) over a range. */
export function useUnifiHistory(hostId: string, range: TimeRange) {
  return useQuery({
    queryKey: unifiKeys.history(hostId, range),
    queryFn: () => {
      const { from, to } = resolveRange(range, Date.now());
      return api.get<MetricSeries>(
        `/api/unifi/${hostId}/history${query({ from: iso(from), to: iso(to), points: 300 })}`,
      );
    },
    placeholderData: keepPreviousData,
    refetchInterval: refreshInterval(range),
  });
}

/** What each client moved over a range, busiest first. */
export function useUnifiClients(hostId: string, range: TimeRange) {
  return useQuery({
    queryKey: unifiKeys.clients(hostId, range),
    queryFn: () => {
      const { from, to } = resolveRange(range, Date.now());
      return api.get<UnifiClientTotal[]>(
        `/api/unifi/${hostId}/clients${query({ from: iso(from), to: iso(to) })}`,
      );
    },
    placeholderData: keepPreviousData,
    refetchInterval: 60_000,
  });
}
