import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, query, type ApiError } from "./client";
import type {
  Alert,
  AlertCounts,
  AlertPage,
  AlertRule,
  AlertRuleRequest,
  AlertSeverity,
  AlertStatus,
} from "./types";

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
  rules: ["alert-rules"] as const,
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

/** The signed-in user's alert rules. */
export function useAlertRules() {
  return useQuery({
    queryKey: alertKeys.rules,
    queryFn: () => api.get<AlertRule[]>("/api/alert-rules"),
  });
}

/** Creates a rule, or replaces the one with the given id. Open alerts may resolve as a result. */
export function useSaveAlertRule() {
  const client = useQueryClient();
  return useMutation<AlertRule, ApiError, { id?: string; rule: AlertRuleRequest }>({
    mutationFn: ({ id, rule }) =>
      id ? api.put<AlertRule>(`/api/alert-rules/${id}`, rule) : api.post<AlertRule>("/api/alert-rules", rule),
    onSuccess: (saved) => {
      client.setQueryData<AlertRule[]>(alertKeys.rules, (rules) =>
        rules?.some((rule) => rule.id === saved.id)
          ? rules.map((rule) => (rule.id === saved.id ? saved : rule))
          : rules && [...rules, saved],
      );
      void client.invalidateQueries({ queryKey: alertKeys.all });
    },
  });
}

/** Deletes a rule. Its open alerts resolve; past ones stay in the history. */
export function useDeleteAlertRule() {
  const client = useQueryClient();
  return useMutation<void, ApiError, string>({
    mutationFn: (id) => api.delete(`/api/alert-rules/${id}`),
    onSuccess: (_, id) => {
      client.setQueryData<AlertRule[]>(alertKeys.rules, (rules) => rules?.filter((rule) => rule.id !== id));
      void client.invalidateQueries({ queryKey: alertKeys.all });
    },
  });
}
