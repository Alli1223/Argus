import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, type ApiError } from "./client";
import type { ApiTokenSummary, CreatedApiToken } from "./types";

export const apiTokenKeys = {
  tokens: ["account", "api-tokens"] as const,
};

/** The signed-in user's API tokens, newest first, revoked ones included. */
export function useApiTokens() {
  return useQuery({
    queryKey: apiTokenKeys.tokens,
    queryFn: () => api.get<ApiTokenSummary[]>("/api/account/api-tokens"),
  });
}

/** Creates a token. The secret is in the response and nowhere else, ever. */
export function useCreateApiToken() {
  const client = useQueryClient();
  return useMutation<CreatedApiToken, ApiError, string>({
    mutationFn: (name) => api.post<CreatedApiToken>("/api/account/api-tokens", { name }),
    onSuccess: () => void client.invalidateQueries({ queryKey: apiTokenKeys.tokens }),
  });
}

/** Stops a token working at once. */
export function useRevokeApiToken() {
  const client = useQueryClient();
  return useMutation<void, ApiError, string>({
    mutationFn: (id) => api.delete(`/api/account/api-tokens/${id}`),
    onSuccess: () => void client.invalidateQueries({ queryKey: apiTokenKeys.tokens }),
  });
}
