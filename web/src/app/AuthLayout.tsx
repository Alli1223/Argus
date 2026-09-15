import { Box, Stack, Text, Title } from "@mantine/core";
import type { ReactNode } from "react";
import { Link, Outlet } from "react-router";
import { Wordmark } from "../components/Wordmark";

/** Sign-in, setup and registration: the wordmark and one form, nothing else. */
export function AuthLayout() {
  return (
    <Box mih="100dvh">
      <Stack w="100%" maw={380} gap={36} px="md" pt="12dvh" pb="xl" mx="auto">
        <Link to="/" aria-label="Argus" style={{ textDecoration: "none", width: "fit-content" }}>
          <Wordmark size="lg" />
        </Link>
        <Outlet />
      </Stack>
    </Box>
  );
}

export function AuthPanel({
  title,
  description,
  children,
}: {
  title: string;
  description?: string;
  children: ReactNode;
}) {
  return (
    <Stack gap="lg">
      <div>
        <Title order={1} fz={24}>
          {title}
        </Title>
        {description && (
          <Text c="dimmed" mt={6}>
            {description}
          </Text>
        )}
      </div>
      {children}
    </Stack>
  );
}
