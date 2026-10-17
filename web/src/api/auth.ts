import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, type ApiError } from "./client";
import type { AuthStatus, Credentials, CurrentUser, NewAccount, TwoFactorRequired } from "./types";

export const authKeys = {
  status: ["auth", "status"] as const,
  me: ["auth", "me"] as const,
};

export function useAuthStatus() {
  return useQuery({
    queryKey: authKeys.status,
    queryFn: () => api.get<AuthStatus>("/api/auth/status"),
    staleTime: 60_000,
  });
}

export function useCurrentUser(enabled = true) {
  return useQuery({
    queryKey: authKeys.me,
    queryFn: () => api.get<CurrentUser>("/api/auth/me"),
    enabled,
    staleTime: 5 * 60_000,
    retry: false,
  });
}

/** Shared by sign-in, setup and registration: start from a clean cache as the new user. */
function useSignedIn() {
  const client = useQueryClient();
  return (user: CurrentUser) => {
    client.clear();
    client.setQueryData(authKeys.me, user);
    client.setQueryData<AuthStatus>(authKeys.status, (status) =>
      status ? { ...status, setupRequired: false } : status,
    );
  };
}

export function isTwoFactorRequired(result: CurrentUser | TwoFactorRequired): result is TwoFactorRequired {
  return "twoFactorRequired" in result && result.twoFactorRequired;
}

/** Checks the password. Accounts with two-factor sign-in then need {@link useLoginTwoFactor}. */
export function useLogin() {
  const signedIn = useSignedIn();
  return useMutation<CurrentUser | TwoFactorRequired, ApiError, Credentials>({
    mutationFn: (credentials) => api.post<CurrentUser | TwoFactorRequired>("/api/auth/login", credentials),
    onSuccess: (result) => {
      if (!isTwoFactorRequired(result)) signedIn(result);
    },
  });
}

/** Finishes signing in with a code from the authenticator app, or a recovery code. */
export function useLoginTwoFactor() {
  const signedIn = useSignedIn();
  return useMutation<CurrentUser, ApiError, { code: string; rememberMe: boolean }>({
    mutationFn: (step) => api.post<CurrentUser>("/api/auth/login/two-factor", step),
    onSuccess: signedIn,
  });
}

export function useSetup() {
  const signedIn = useSignedIn();
  return useMutation<CurrentUser, ApiError, NewAccount>({
    mutationFn: (account) => api.post<CurrentUser>("/api/auth/setup", account),
    onSuccess: signedIn,
  });
}

export function useRegister() {
  const signedIn = useSignedIn();
  return useMutation<CurrentUser, ApiError, NewAccount>({
    mutationFn: (account) => api.post<CurrentUser>("/api/auth/register", account),
    onSuccess: signedIn,
  });
}

export function useLogout() {
  const client = useQueryClient();
  return useMutation<void, ApiError>({
    mutationFn: () => api.post("/api/auth/logout"),
    onSettled: () => client.clear(),
  });
}

/** Only allows returning to a page on this site, never to another origin. */
export function safeNextPath(next: string | null): string {
  return next && next.startsWith("/") && !next.startsWith("//") ? next : "/";
}
