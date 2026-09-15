import { Button, Center, Loader, Stack, Text, Title } from "@mantine/core";
import type { ReactNode } from "react";
import { ApiError } from "../api/client";

export function FullPageLoader() {
  return (
    <Center mih="60dvh">
      <Loader size="sm" />
    </Center>
  );
}

interface MessageScreenProps {
  title: string;
  children?: ReactNode;
  action?: ReactNode;
}

/** A calm, centred message for empty, missing and failed states. */
export function MessageScreen({ title, children, action }: MessageScreenProps) {
  return (
    <Center mih="50dvh" px="md">
      <Stack gap="sm" maw={460}>
        <Title order={2} fz={22}>
          {title}
        </Title>
        {children && <Text c="dimmed">{children}</Text>}
        {action}
      </Stack>
    </Center>
  );
}

export function ErrorScreen({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const title = error instanceof ApiError ? error.title : "Something went wrong";
  const detail = error instanceof Error ? error.message : undefined;
  return (
    <MessageScreen
      title={title}
      action={
        onRetry && (
          <Button variant="light" onClick={onRetry} w="fit-content">
            Try again
          </Button>
        )
      }
    >
      {detail !== title ? detail : undefined}
    </MessageScreen>
  );
}
