import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, type ApiError } from "./client";
import type { HostPower, PowerActionInfo, PowerSettingsRequest } from "./types";

export const powerKeys = {
  host: (hostId: string) => ["power", hostId] as const,
};

export type PowerCommand = "shutdown" | "restart" | "wake";

/** What Argus can do about a host's power, and the latest times it did. */
export function useHostPower(hostId: string) {
  return useQuery({
    queryKey: powerKeys.host(hostId),
    queryFn: () => api.get<HostPower>(`/api/hosts/${hostId}/power`),
    refetchInterval: 30_000,
  });
}

/** Shuts the host down, restarts it or wakes it; the answer says how it went. */
export function usePowerCommand(hostId: string) {
  const queryClient = useQueryClient();
  return useMutation<PowerActionInfo, ApiError, PowerCommand>({
    mutationFn: (command) => api.post<PowerActionInfo>(`/api/hosts/${hostId}/power/${command}`),
    onSettled: () => queryClient.invalidateQueries({ queryKey: powerKeys.host(hostId) }),
  });
}

export function useUpdatePowerSettings(hostId: string) {
  const queryClient = useQueryClient();
  return useMutation<HostPower, ApiError, PowerSettingsRequest>({
    mutationFn: (settings) => api.put<HostPower>(`/api/hosts/${hostId}/power/settings`, settings),
    onSuccess: (power) => queryClient.setQueryData(powerKeys.host(hostId), power),
  });
}
