import type { ReactNode } from "react";
import { Navigate, useLocation } from "react-router";
import { useAuthStatus, useCurrentUser } from "../api/auth";
import { ApiError } from "../api/client";
import { ErrorScreen, FullPageLoader } from "../components/Screens";

/** Sends visitors to first-run setup or to sign in; renders the page once there is a session. */
export function RequireAuth({ children }: { children: ReactNode }) {
  const location = useLocation();
  const status = useAuthStatus();
  const setupRequired = status.data?.setupRequired ?? false;
  const me = useCurrentUser(status.isSuccess && !setupRequired);

  if (status.isPending) return <FullPageLoader />;
  if (status.isError) return <ErrorScreen error={status.error} onRetry={() => void status.refetch()} />;
  if (setupRequired) return <Navigate to="/setup" replace />;

  if (me.isPending) return <FullPageLoader />;
  if (me.isError) {
    if (me.error instanceof ApiError && me.error.status === 401) {
      const next = encodeURIComponent(location.pathname + location.search);
      return <Navigate to={`/login?next=${next}`} replace />;
    }
    return <ErrorScreen error={me.error} onRetry={() => void me.refetch()} />;
  }

  return children;
}

/** Keeps administrator pages away from everyone else. */
export function RequireAdmin({ children }: { children: ReactNode }) {
  const me = useCurrentUser();
  return me.data?.isAdmin ? children : <Navigate to="/" replace />;
}
