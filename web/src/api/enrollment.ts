import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, type ApiError } from "./client";
import type { CreateEnrollmentToken, CreatedEnrollmentToken, EnrollmentTokenSummary } from "./types";

export const enrollmentKeys = {
  tokens: ["enrollment-tokens"] as const,
};

/** The signed-in user's enrollment tokens, newest first, revoked and expired ones included. */
export function useEnrollmentTokens(refetchInterval: number | false = false) {
  return useQuery({
    queryKey: enrollmentKeys.tokens,
    queryFn: () => api.get<EnrollmentTokenSummary[]>("/api/enrollment-tokens"),
    refetchInterval,
  });
}

/** Creates a token. The secret is in the response and nowhere else, ever. */
export function useCreateEnrollmentToken() {
  const client = useQueryClient();
  return useMutation<CreatedEnrollmentToken, ApiError, CreateEnrollmentToken>({
    mutationFn: (request) => api.post<CreatedEnrollmentToken>("/api/enrollment-tokens", request),
    onSuccess: () => void client.invalidateQueries({ queryKey: enrollmentKeys.tokens }),
  });
}

/** Stops a token registering more machines. Machines that already registered are not affected. */
export function useRevokeEnrollmentToken() {
  const client = useQueryClient();
  return useMutation<void, ApiError, string>({
    mutationFn: (id) => api.delete(`/api/enrollment-tokens/${id}`),
    onSuccess: () => void client.invalidateQueries({ queryKey: enrollmentKeys.tokens }),
  });
}
