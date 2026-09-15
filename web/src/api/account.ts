import { useMutation, useQueryClient } from "@tanstack/react-query";
import { authKeys } from "./auth";
import { api, type ApiError } from "./client";
import type { CurrentUser } from "./types";

export interface PasswordChange {
  currentPassword: string;
  newPassword: string;
}

/** Changes the signed-in person's display name. */
export function useUpdateProfile() {
  const client = useQueryClient();
  return useMutation<CurrentUser, ApiError, { displayName: string }>({
    mutationFn: (profile) => api.put<CurrentUser>("/api/account/profile", profile),
    onSuccess: (user) => client.setQueryData(authKeys.me, user),
  });
}

/** Changes the password. Every other session is signed out; this one stays signed in. */
export function useChangePassword() {
  return useMutation<void, ApiError, PasswordChange>({
    mutationFn: (change) => api.post("/api/account/password", change),
  });
}
