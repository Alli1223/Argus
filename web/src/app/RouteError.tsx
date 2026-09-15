import { Anchor, Button, Group } from "@mantine/core";
import { isRouteErrorResponse, Link, useRouteError } from "react-router";
import { MessageScreen } from "../components/Screens";

/** A page's code failed to load, usually because Argus was updated after this tab was opened. */
function isStaleBuild(error: unknown): boolean {
  return (
    error instanceof Error &&
    /dynamically imported module|importing a module script failed/i.test(error.message)
  );
}

export function RouteError() {
  const error = useRouteError();

  if (isRouteErrorResponse(error) && error.status === 404) {
    return <NotFoundPage />;
  }

  const reload = (
    <Button variant="light" onClick={() => window.location.reload()}>
      Reload the page
    </Button>
  );

  if (isStaleBuild(error)) {
    return (
      <MessageScreen title="Argus has been updated" action={reload}>
        This tab is still running the previous version. Reload it to continue.
      </MessageScreen>
    );
  }

  return (
    <MessageScreen
      title="Something went wrong"
      action={
        <Group gap="md">
          {reload}
          <Anchor component={Link} to="/">
            Go to the overview
          </Anchor>
        </Group>
      }
    >
      {error instanceof Error ? error.message : "The page failed to load."}
    </MessageScreen>
  );
}

export function NotFoundPage() {
  return (
    <MessageScreen
      title="This page doesn't exist"
      action={
        <Anchor component={Link} to="/">
          Go to the overview
        </Anchor>
      }
    >
      The link may be old, or the host or rule it pointed to was removed.
    </MessageScreen>
  );
}
