import { Anchor, Button, Group } from "@mantine/core";
import { isRouteErrorResponse, Link, useRouteError } from "react-router";
import { MessageScreen } from "../components/Screens";

export function RouteError() {
  const error = useRouteError();

  if (isRouteErrorResponse(error) && error.status === 404) {
    return <NotFoundPage />;
  }

  return (
    <MessageScreen
      title="Something went wrong"
      action={
        <Group>
          <Button variant="light" onClick={() => window.location.reload()}>
            Reload the page
          </Button>
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
