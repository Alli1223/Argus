import { createBrowserRouter } from "react-router";
import { LoginPage } from "../pages/auth/LoginPage";
import { RegisterPage } from "../pages/auth/RegisterPage";
import { SetupPage } from "../pages/auth/SetupPage";
import { HostPage } from "../pages/host/HostPage";
import { HostsPage } from "../pages/hosts/HostsPage";
import { OverviewPage } from "../pages/overview/OverviewPage";
import { AccountPage, AlertsPage, RulesPage, UsersPage } from "../pages/Placeholders";
import { SystemsPage } from "../pages/systems/SystemsPage";
import { AppLayout } from "./AppLayout";
import { AuthLayout } from "./AuthLayout";
import { RequireAdmin, RequireAuth } from "./RequireAuth";
import { NotFoundPage, RouteError } from "./RouteError";

export const router = createBrowserRouter([
  {
    element: <AuthLayout />,
    errorElement: <RouteError />,
    children: [
      { path: "/login", element: <LoginPage /> },
      { path: "/setup", element: <SetupPage /> },
      { path: "/register", element: <RegisterPage /> },
    ],
  },
  {
    path: "/",
    element: (
      <RequireAuth>
        <AppLayout />
      </RequireAuth>
    ),
    errorElement: <RouteError />,
    children: [
      { index: true, element: <OverviewPage /> },
      { path: "hosts", element: <HostsPage /> },
      { path: "hosts/:hostId", element: <HostPage /> },
      { path: "alerts", element: <AlertsPage /> },
      { path: "rules", element: <RulesPage /> },
      { path: "systems", element: <SystemsPage /> },
      {
        path: "users",
        element: (
          <RequireAdmin>
            <UsersPage />
          </RequireAdmin>
        ),
      },
      { path: "account", element: <AccountPage /> },
      { path: "*", element: <NotFoundPage /> },
    ],
  },
]);
