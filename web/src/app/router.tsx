import { createBrowserRouter, Outlet } from "react-router";
import { LoginPage } from "../pages/auth/LoginPage";
import { RegisterPage } from "../pages/auth/RegisterPage";
import { SetupPage } from "../pages/auth/SetupPage";
import { HostsPage } from "../pages/hosts/HostsPage";
import { OverviewPage } from "../pages/overview/OverviewPage";
import { AppLayout } from "./AppLayout";
import { AuthLayout } from "./AuthLayout";
import { RequireAdmin, RequireAuth } from "./RequireAuth";
import { NotFoundPage, RouteError } from "./RouteError";

// The overview and the hosts list are where people land, so they ship with the app. Every other
// page, with what it brings (the charting library for the host page), loads when first opened.
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
      {
        // A page that fails shows its error inside the app shell, so the navigation stays usable.
        errorElement: <RouteError />,
        children: [
          { index: true, element: <OverviewPage /> },
          { path: "hosts", element: <HostsPage /> },
          {
            path: "hosts/:hostId",
            lazy: async () => ({ Component: (await import("../pages/host/HostPage")).HostPage }),
          },
          {
            path: "temperatures",
            lazy: async () => ({
              Component: (await import("../pages/temperatures/TemperaturesPage")).TemperaturesPage,
            }),
          },
          {
            path: "alerts",
            lazy: async () => ({ Component: (await import("../pages/alerts/AlertsPage")).AlertsPage }),
          },
          {
            path: "rules",
            lazy: async () => ({ Component: (await import("../pages/rules/RulesPage")).RulesPage }),
          },
          {
            path: "notifications",
            lazy: async () => ({
              Component: (await import("../pages/notifications/NotificationsPage")).NotificationsPage,
            }),
          },
          {
            path: "systems",
            lazy: async () => ({ Component: (await import("../pages/systems/SystemsPage")).SystemsPage }),
          },
          {
            path: "users",
            element: (
              <RequireAdmin>
                <Outlet />
              </RequireAdmin>
            ),
            children: [
              {
                index: true,
                lazy: async () => ({ Component: (await import("../pages/users/UsersPage")).UsersPage }),
              },
            ],
          },
          {
            path: "settings",
            element: (
              <RequireAdmin>
                <Outlet />
              </RequireAdmin>
            ),
            children: [
              {
                index: true,
                lazy: async () => ({
                  Component: (await import("../pages/settings/SettingsPage")).SettingsPage,
                }),
              },
            ],
          },
          {
            path: "account",
            lazy: async () => ({ Component: (await import("../pages/account/AccountPage")).AccountPage }),
          },
          { path: "*", element: <NotFoundPage /> },
        ],
      },
    ],
  },
]);
