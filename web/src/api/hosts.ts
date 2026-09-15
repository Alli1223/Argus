import { useQuery } from "@tanstack/react-query";
import { api } from "./client";
import type { DashboardSummary, HostDetail, HostSummary } from "./types";

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
