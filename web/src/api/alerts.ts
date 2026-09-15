import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, query, type ApiError } from "./client";
import type { Alert, AlertCounts, AlertPage, AlertSeverity, AlertStatus } from "./types";

export type AlertFilter = {
  status?: AlertStatus;
  hostId?: string;
  severity?: AlertSeverity;
  page?: number;
  pageSize?: number;
};

export const alertKeys = {
  all: ["alerts"] as const,
  counts: ["alerts", "counts"] as const,
  list: (filter: AlertFilter) => ["alerts", "list", filter] as const,
};

/** Firing alerts by severity, for the header. */
export function useAlertCounts() {
  return useQuery({
    queryKey: alertKeys.counts,
    queryFn: () => api.get<AlertCounts>("/api/alerts/summary"),
    refetchInterval: 30_000,
  });
}

export function useAlerts(filter: AlertFilter) {
  return useQuery({
    queryKey: alertKeys.list(filter),
    queryFn: () => api.get<AlertPage>(`/api/alerts${query({ ...filter })}`),
    refetchInterval: 30_000,
    placeholderData: keepPreviousData,
  });
}

export function useAcknowledgeAlert() {
  const client = useQueryClient();
  return useMutation<Alert, ApiError, string>({
    mutationFn: (id) => api.post<Alert>(`/api/alerts/${id}/acknowledge`),
    onSuccess: () => void client.invalidateQueries({ queryKey: alertKeys.all }),
  });
}
