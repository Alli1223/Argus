import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { alertKeys } from "./alerts";
import { api, type ApiError } from "./client";
import type { DashboardSummary, HostDetail, HostSummary, UpdateHost } from "./types";

export const hostKeys = {
  all: ["hosts"] as const,
  list: ["hosts", "list"] as const,
  detail: (id: string) => ["hosts", "detail", id] as const,
};

export const dashboardKeys = {
  summary: ["dashboard", "summary"] as const,
};

export function useHosts() {
  return useQuery({
    queryKey: hostKeys.list,
    queryFn: () => api.get<HostSummary[]>("/api/hosts"),
    refetchInterval: 30_000,
  });
}

export function useHost(id: string) {
  return useQuery({
    queryKey: hostKeys.detail(id),
    queryFn: () => api.get<HostDetail>(`/api/hosts/${id}`),
    refetchInterval: 30_000,
  });
}

export function useDashboardSummary() {
  return useQuery({
    queryKey: dashboardKeys.summary,
    queryFn: () => api.get<DashboardSummary>("/api/dashboard/summary"),
    refetchInterval: 30_000,
  });
}

/** Renames a host or changes its tags or notes. */
export function useUpdateHost(id: string) {
  const client = useQueryClient();
  return useMutation<HostDetail, ApiError, UpdateHost>({
    mutationFn: (changes) => api.patch<HostDetail>(`/api/hosts/${id}`, changes),
    onSuccess: (host) => {
      client.setQueryData(hostKeys.detail(id), host);
      void client.invalidateQueries({ queryKey: hostKeys.list });
      void client.invalidateQueries({ queryKey: dashboardKeys.summary });
    },
  });
}

/** Deletes a host with its history and alerts. The caller moves away from the host's page. */
export function useDeleteHost(id: string) {
  const client = useQueryClient();
  return useMutation<void, ApiError, void>({
    mutationFn: () => api.delete(`/api/hosts/${id}`),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: hostKeys.list });
      void client.invalidateQueries({ queryKey: dashboardKeys.summary });
      void client.invalidateQueries({ queryKey: alertKeys.all });
    },
  });
}
