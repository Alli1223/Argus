import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, type ApiError } from "./client";
import { notificationKeys } from "./notifications";
import type { EmailSettings, EmailSettingsRequest } from "./types";

export const settingsKeys = {
  email: ["settings", "email"] as const,
};

/** The mail server Argus sends notifications through. Administrators only. */
export function useEmailSettings() {
  return useQuery({
    queryKey: settingsKeys.email,
    queryFn: () => api.get<EmailSettings>("/api/settings/email"),
  });
}

/** Saves the mail server for the whole server, in place of anything the Compose file sets. */
export function useSaveEmailSettings() {
  const client = useQueryClient();
  return useMutation<EmailSettings, ApiError, EmailSettingsRequest>({
    mutationFn: (settings) => api.put<EmailSettings>("/api/settings/email", settings),
    onSuccess: (saved) => {
      client.setQueryData(settingsKeys.email, saved);
      // Whether email channels can send has just changed.
      void client.invalidateQueries({ queryKey: notificationKeys.support });
    },
  });
}

/** Forgets the settings saved here, so the ones in the Compose file are used again. */
export function useForgetEmailSettings() {
  const client = useQueryClient();
  return useMutation<void, ApiError>({
    mutationFn: () => api.delete("/api/settings/email"),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: settingsKeys.email });
      void client.invalidateQueries({ queryKey: notificationKeys.support });
    },
  });
}

/** Sends a test email with the saved settings. It fails with what the mail server said. */
export function useSendTestEmail() {
  return useMutation<void, ApiError, string>({
    mutationFn: (to) => api.post("/api/settings/email/test", { to }),
  });
}
