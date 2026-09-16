import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, type ApiError } from "./client";
import { hostKeys } from "./hosts";
import type { HostDetail, ServerSelfUpdate, ServerUpdateInfo } from "./types";

export const updateKeys = {
  server: ["updates", "server"] as const,
  selfUpdate: ["updates", "self"] as const,
};

/** This server's version against the latest release. Administrators only; the server checks every few hours. */
export function useServerUpdate(enabled: boolean) {
  return useQuery({
    queryKey: updateKeys.server,
    queryFn: () => api.get<ServerUpdateInfo>("/api/updates"),
    enabled,
    staleTime: 60 * 60 * 1000,
  });
}

/** Asks the server to look for a new release now. */
export function useCheckForUpdates() {
  const client = useQueryClient();
  return useMutation<ServerUpdateInfo, ApiError>({
    mutationFn: () => api.post<ServerUpdateInfo>("/api/updates/check"),
    onSuccess: (info) => {
      client.setQueryData(updateKeys.server, info);
      void client.invalidateQueries({ queryKey: hostKeys.all });
    },
  });
}

const isBusy = (state: ServerSelfUpdate | undefined) =>
  Boolean(state?.pendingVersion || state?.lastRun?.inProgress);

/**
 * Whether Argus can update itself, and how its latest update went. While one runs this checks every
 * two seconds, and keeps checking while the server restarts and cannot answer.
 */
export function useServerSelfUpdate() {
  return useQuery({
    queryKey: updateKeys.selfUpdate,
    queryFn: () => api.get<ServerSelfUpdate>("/api/updates/server"),
    retry: false,
    refetchInterval: (query) => (isBusy(query.state.data) ? 2_000 : 30_000),
  });
}

/** Asks the updater service to install a release. */
export function useStartServerUpdate() {
  const client = useQueryClient();
  return useMutation<ServerSelfUpdate, ApiError, string>({
    mutationFn: (version) => api.post<ServerSelfUpdate>("/api/updates/server", { version }),
    onSuccess: (state) => client.setQueryData(updateKeys.selfUpdate, state),
  });
}

/** Asks the host's agent to update to the latest release. */
export function useRequestAgentUpdate(hostId: string) {
  const client = useQueryClient();
  return useMutation<HostDetail, ApiError>({
    mutationFn: () => api.post<HostDetail>(`/api/hosts/${hostId}/agent-update`),
    onSuccess: (host) => {
      client.setQueryData(hostKeys.detail(hostId), host);
      void client.invalidateQueries({ queryKey: hostKeys.list });
    },
  });
}

/** Withdraws an update request, and forgets why the last attempt failed. */
export function useCancelAgentUpdate(hostId: string) {
  const client = useQueryClient();
  return useMutation<HostDetail, ApiError>({
    mutationFn: () =>
      api.delete(`/api/hosts/${hostId}/agent-update`).then(() => api.get<HostDetail>(`/api/hosts/${hostId}`)),
    onSuccess: (host) => {
      client.setQueryData(hostKeys.detail(hostId), host);
      void client.invalidateQueries({ queryKey: hostKeys.list });
    },
  });
}

/** Asks every out-of-date agent you can see to update. */
export function useRequestAllAgentUpdates() {
  const client = useQueryClient();
  return useMutation<{ requested: number }, ApiError>({
    mutationFn: () => api.post<{ requested: number }>("/api/hosts/agent-updates"),
    onSuccess: () => void client.invalidateQueries({ queryKey: hostKeys.all }),
  });
}
