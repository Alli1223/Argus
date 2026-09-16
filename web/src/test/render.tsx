import { MantineProvider } from "@mantine/core";
import { ModalsProvider } from "@mantine/modals";
import { Notifications } from "@mantine/notifications";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactElement } from "react";
import { MemoryRouter } from "react-router";
import { cssVariablesResolver, theme } from "../theme";

/**
 * Renders a piece of the app inside its providers: the theme (without transitions or portals), a
 * fresh query cache that does not retry, dialogs, toasts and a router.
 */
export function renderWithApp(ui: ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  const user = userEvent.setup();
  const result = render(
    <MantineProvider theme={theme} cssVariablesResolver={cssVariablesResolver} env="test">
      <Notifications />
      <QueryClientProvider client={client}>
        <ModalsProvider>
          <MemoryRouter>{ui}</MemoryRouter>
        </ModalsProvider>
      </QueryClientProvider>
    </MantineProvider>,
  );
  return { ...result, user, client };
}
