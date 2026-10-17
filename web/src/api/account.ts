import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { authKeys } from "./auth";
import { api, type ApiError } from "./client";
import type { CurrentUser, TwoFactorSetup, TwoFactorStatus } from "./types";

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

const twoFactorKey = ["account", "two-factor"] as const;

/** Whether two-factor sign-in is on, and how many recovery codes are left. */
export function useTwoFactorStatus() {
  return useQuery({
    queryKey: twoFactorKey,
    queryFn: () => api.get<TwoFactorStatus>("/api/account/two-factor"),
  });
}

/** Two-factor changes show on the account too, so both are refreshed. */
function useTwoFactorChanged() {
  const client = useQueryClient();
  return () => {
    void client.invalidateQueries({ queryKey: twoFactorKey });
    void client.invalidateQueries({ queryKey: authKeys.me });
  };
}

/** Starts setting up with a new key. Nothing changes at sign-in until a code is confirmed. */
export function useStartTwoFactorSetup() {
  return useMutation<TwoFactorSetup, ApiError>({
    mutationFn: () => api.post<TwoFactorSetup>("/api/account/two-factor/setup"),
  });
}

/** Turns two-factor sign-in on with a code from the app; answers with the recovery codes, shown once. */
export function useEnableTwoFactor() {
  const changed = useTwoFactorChanged();
  return useMutation<{ recoveryCodes: string[] }, ApiError, string>({
    mutationFn: (code) => api.post<{ recoveryCodes: string[] }>("/api/account/two-factor/enable", { code }),
    onSuccess: changed,
  });
}

export function useDisableTwoFactor() {
  const changed = useTwoFactorChanged();
  return useMutation<void, ApiError, string>({
    mutationFn: (password) => api.post("/api/account/two-factor/disable", { password }),
    onSuccess: changed,
  });
}

/** New recovery codes; the old ones stop working. */
export function useRegenerateRecoveryCodes() {
  const changed = useTwoFactorChanged();
  return useMutation<{ recoveryCodes: string[] }, ApiError, string>({
    mutationFn: (password) =>
      api.post<{ recoveryCodes: string[] }>("/api/account/two-factor/recovery-codes", { password }),
    onSuccess: changed,
  });
}
