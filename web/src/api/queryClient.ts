import { MutationCache, QueryCache, QueryClient } from "@tanstack/react-query";
import { ApiError } from "./client";

/** When any call reports a lost session, re-check it so the app sends the user to sign in.
 * Skip re-checking when the auth/me query itself 401s: RequireAuth handles that directly, and
 * invalidating it here would cause an infinite refetch loop (401 → invalidate → 401 → …). */
function onQueryError(error: Error, query: { queryKey: readonly unknown[] }) {
  const [k0, k1] = query.queryKey;
  if (error instanceof ApiError && error.status === 401 && !(k0 === "auth" && k1 === "me")) {
    void queryClient.invalidateQueries({ queryKey: ["auth", "me"] });
  }
}

function onMutationError(error: Error) {
  if (error instanceof ApiError && error.status === 401) {
    void queryClient.invalidateQueries({ queryKey: ["auth", "me"] });
  }
}

export const queryClient: QueryClient = new QueryClient({
  queryCache: new QueryCache({ onError: onQueryError }),
  mutationCache: new MutationCache({ onError: onMutationError }),
  defaultOptions: {
    queries: {
      staleTime: 10_000,
      // Client errors (not found, forbidden, signed out) will not fix themselves by retrying.
      retry: (failureCount, error) =>
        !(error instanceof ApiError && error.status >= 400 && error.status < 500) && failureCount < 2,
    },
  },
});
