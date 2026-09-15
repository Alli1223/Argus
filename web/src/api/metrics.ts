import { useQuery } from "@tanstack/react-query";
import { refreshInterval, resolveRange, type TimeRange } from "../lib/timeRange";
import { api, query } from "./client";
import type { FilesystemSnapshot, MetricSeries, ProcessSnapshot, ServiceStatus } from "./types";

type SeriesKey = readonly [scope: "hosts", kind: string, id: string, range: TimeRange];

export const metricKeys = {
  host: (id: string, range: TimeRange): SeriesKey => ["hosts", "metrics", id, range],
  filesystemHistory: (id: string, range: TimeRange): SeriesKey => ["hosts", "filesystem-history", id, range],
  network: (id: string, range: TimeRange): SeriesKey => ["hosts", "network", id, range],
  filesystems: (id: string) => ["hosts", "filesystems", id] as const,
  processes: (id: string) => ["hosts", "processes", id] as const,
  services: (id: string) => ["hosts", "services", id] as const,
};

const iso = (seconds: number) => new Date(seconds * 1000).toISOString();

/**
 * A series over a range. Live ranges refetch as new readings arrive; while another range of the
 * same host loads, the previous one stays on screen.
 */
function useSeries(key: SeriesKey, path: string) {
  const [, , id, range] = key;
  return useQuery({
    queryKey: key,
    queryFn: () => {
      const { from, to } = resolveRange(range, Date.now());
      return api.get<MetricSeries>(`${path}${query({ from: iso(from), to: iso(to), points: 300 })}`);
    },
    placeholderData: (previous, previousQuery) => (previousQuery?.queryKey[2] === id ? previous : undefined),
    refetchInterval: refreshInterval(range),
  });
}

/** CPU, memory, load, disk and network history of a host. */
export function useHostMetrics(id: string, range: TimeRange) {
  return useSeries(metricKeys.host(id, range), `/api/hosts/${id}/metrics`);
}

/** Used share of each filesystem over time, keyed by mount point. */
export function useFilesystemHistory(id: string, range: TimeRange) {
  return useSeries(metricKeys.filesystemHistory(id, range), `/api/hosts/${id}/filesystems/history`);
}

/** Traffic per interface over time, keyed `rx:{interface}` and `tx:{interface}`. */
export function useNetworkHistory(id: string, range: TimeRange) {
  return useSeries(metricKeys.network(id, range), `/api/hosts/${id}/network`);
}

/** The filesystems in the host's newest report. */
export function useHostFilesystems(id: string) {
  return useQuery({
    queryKey: metricKeys.filesystems(id),
    queryFn: () => api.get<FilesystemSnapshot[]>(`/api/hosts/${id}/filesystems`),
    refetchInterval: 60_000,
  });
}

/** Services failing in the host's newest service check. Agents check about once a minute. */
export function useHostServices(id: string) {
  return useQuery({
    queryKey: metricKeys.services(id),
    queryFn: () => api.get<ServiceStatus>(`/api/hosts/${id}/services`),
    refetchInterval: 60_000,
  });
}

/** The busiest processes in the host's newest report; null until the agent has sent a list. */
export function useHostProcesses(id: string) {
  return useQuery({
    queryKey: metricKeys.processes(id),
    queryFn: async () => (await api.get<ProcessSnapshot | undefined>(`/api/hosts/${id}/processes`)) ?? null,
    refetchInterval: 30_000,
  });
}
