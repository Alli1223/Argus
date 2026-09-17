import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { refreshInterval, resolveRange, type TimeRange } from "../lib/timeRange";
import { api, query } from "./client";
import type { ContainerDetail, ContainerHost, HostContainers, MetricSeries } from "./types";

// Agents report changes to containers with their next reading, so these follow at about that pace.
const LIVE = 15_000;

export const containerKeys = {
  all: ["containers"] as const,
  fleet: ["containers", "fleet"] as const,
  host: (hostId: string) => ["containers", "host", hostId] as const,
  detail: (hostId: string, name: string) => ["containers", "detail", hostId, name] as const,
  history: (hostId: string, name: string, range: TimeRange) =>
    ["containers", "history", hostId, name, range] as const,
};

const path = (hostId: string, name: string) => `/api/hosts/${hostId}/containers/${encodeURIComponent(name)}`;

/** Every visible host that reports containers, with its containers. */
export function useContainerFleet() {
  return useQuery({
    queryKey: containerKeys.fleet,
    queryFn: () => api.get<ContainerHost[]>("/api/containers"),
    refetchInterval: LIVE,
  });
}

/** One host's containers, from its agent's newest report. */
export function useHostContainers(hostId: string) {
  return useQuery({
    queryKey: containerKeys.host(hostId),
    queryFn: () => api.get<HostContainers>(`/api/hosts/${hostId}/containers`),
    refetchInterval: LIVE,
  });
}

/** One container with its latest events. */
export function useContainer(hostId: string, name: string) {
  return useQuery({
    queryKey: containerKeys.detail(hostId, name),
    queryFn: () => api.get<ContainerDetail>(path(hostId, name)),
    refetchInterval: LIVE,
  });
}

/** A container's CPU, memory and traffic over a range: series cpu, cpuMax, memory, memoryLimit, netRx and netTx. */
export function useContainerHistory(hostId: string, name: string, range: TimeRange) {
  return useQuery({
    queryKey: containerKeys.history(hostId, name, range),
    queryFn: () => {
      const { from, to } = resolveRange(range, Date.now());
      const iso = (seconds: number) => new Date(seconds * 1000).toISOString();
      return api.get<MetricSeries>(
        `${path(hostId, name)}/metrics${query({ from: iso(from), to: iso(to), points: 300 })}`,
      );
    },
    placeholderData: keepPreviousData,
    refetchInterval: refreshInterval(range),
  });
}
