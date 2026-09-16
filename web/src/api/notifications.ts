import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, type ApiError } from "./client";
import type { NotificationChannel, NotificationChannelRequest, NotificationSupport } from "./types";

export const notificationKeys = {
  channels: ["notification-channels"] as const,
  support: ["notification-support"] as const,
};

/** The signed-in user's notification channels, each with how its latest notification went. */
export function useNotificationChannels() {
  return useQuery({
    queryKey: notificationKeys.channels,
    queryFn: () => api.get<NotificationChannel[]>("/api/notification-channels"),
    refetchInterval: 60_000,
  });
}

/** What the server can send. It only changes when the server restarts with new settings. */
export function useNotificationSupport() {
  return useQuery({
    queryKey: notificationKeys.support,
    queryFn: () => api.get<NotificationSupport>("/api/notification-channels/support"),
    staleTime: Infinity,
  });
}

/** Creates a channel, or replaces the one with the given id. */
export function useSaveNotificationChannel() {
  const client = useQueryClient();
  return useMutation<NotificationChannel, ApiError, { id?: string; channel: NotificationChannelRequest }>({
    mutationFn: ({ id, channel }) =>
      id
        ? api.put<NotificationChannel>(`/api/notification-channels/${id}`, channel)
        : api.post<NotificationChannel>("/api/notification-channels", channel),
    onSuccess: (saved) =>
      client.setQueryData<NotificationChannel[]>(notificationKeys.channels, (channels) =>
        channels?.some((channel) => channel.id === saved.id)
          ? channels.map((channel) => (channel.id === saved.id ? saved : channel))
          : channels && [...channels, saved].sort((a, b) => a.name.localeCompare(b.name)),
      ),
  });
}

/** Deletes a channel, with any notifications still waiting to go out on it. */
export function useDeleteNotificationChannel() {
  const client = useQueryClient();
  return useMutation<void, ApiError, string>({
    mutationFn: (id) => api.delete(`/api/notification-channels/${id}`),
    onSuccess: (_, id) =>
      client.setQueryData<NotificationChannel[]>(notificationKeys.channels, (channels) =>
        channels?.filter((channel) => channel.id !== id),
      ),
  });
}

/** Sends a test message straight away. It fails with the reason when the message did not go out. */
export function useTestNotificationChannel() {
  return useMutation<void, ApiError, string>({
    mutationFn: (id) => api.post(`/api/notification-channels/${id}/test`),
  });
}
