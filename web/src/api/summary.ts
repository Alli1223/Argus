import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { api, query } from "./client";
import type { SummaryPeriod, UsageSummary } from "./types";

export const summaryKeys = {
  usage: (period: SummaryPeriod, count: number, timeZone: string) =>
    ["summary", period, count, timeZone] as const,
};

/** The browser's time zone, so days and months start at local midnight. */
export function browserTimeZone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
}

/**
 * Totals and averages for every visible host by day, month or year. The summary comes from hourly
 * rollups, so refreshing every few minutes is plenty.
 */
export function useUsageSummary(period: SummaryPeriod, count: number, timeZone = browserTimeZone()) {
  return useQuery({
    queryKey: summaryKeys.usage(period, count, timeZone),
    queryFn: () => api.get<UsageSummary>(`/api/summary${query({ period, count, timeZone })}`),
    placeholderData: keepPreviousData,
    refetchInterval: 5 * 60_000,
  });
}
