import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, type ApiError } from "./client";
import type { HostProxmox } from "./types";

export const proxmoxKeys = {
  host: (hostId: string) => ["proxmox", hostId] as const,
};

export type GuestAction = "start" | "shutdown" | "reboot" | "stop";

/** A host's Proxmox VE guests, at about the pace its agent reports them. */
export function useHostProxmox(hostId: string) {
  return useQuery({
    queryKey: proxmoxKeys.host(hostId),
    queryFn: () => api.get<HostProxmox>(`/api/hosts/${hostId}/proxmox`),
    refetchInterval: 15_000,
  });
}

/** Starts, shuts down, restarts or stops a guest through its host's agent, then fetches the guests again. */
export function useGuestAction(hostId: string) {
  const client = useQueryClient();
  return useMutation<void, ApiError, { guestId: number; action: GuestAction }>({
    mutationFn: ({ guestId, action }) => api.post(`/api/hosts/${hostId}/proxmox/guests/${guestId}/${action}`),
    onSettled: () => void client.invalidateQueries({ queryKey: proxmoxKeys.host(hostId) }),
  });
}
