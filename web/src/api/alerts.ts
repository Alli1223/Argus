import { useQuery } from "@tanstack/react-query";
import { api } from "./client";
import type { AlertCounts } from "./types";

export const alertKeys = {
  all: ["alerts"] as const,
  counts: ["alerts", "counts"] as const,
};

/** Firing alerts by severity, for the header. Refreshed every half minute. */
export function useAlertCounts() {
  return useQuery({
    queryKey: alertKeys.counts,
    queryFn: () => api.get<AlertCounts>("/api/alerts/summary"),
    refetchInterval: 30_000,
  });
}
