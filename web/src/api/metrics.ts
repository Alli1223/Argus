import { useQuery } from "@tanstack/react-query";
import { refreshInterval, resolveRange, type TimeRange } from "../lib/timeRange";
import { api, query } from "./client";
import type { MetricSeries } from "./types";

export const metricKeys = {
  host: (id: string, range: TimeRange) => ["hosts", "metrics", id, range] as const,
};

const iso = (seconds: number) => new Date(seconds * 1000).toISOString();

/**
 * A host's resource history over a range. Live ranges refetch as new readings arrive; while another
 * range of the same host loads, the previous one stays on screen.
 */
export function useHostMetrics(id: string, range: TimeRange) {
  return useQuery({
    queryKey: metricKeys.host(id, range),
    queryFn: () => {
      const { from, to } = resolveRange(range, Date.now());
      return api.get<MetricSeries>(
        `/api/hosts/${id}/metrics${query({ from: iso(from), to: iso(to), points: 300 })}`,
      );
    },
    placeholderData: (previous, previousQuery) => (previousQuery?.queryKey[2] === id ? previous : undefined),
    refetchInterval: refreshInterval(range),
  });
}
