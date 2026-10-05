import {
  Alert,
  Anchor,
  Badge,
  Box,
  Button,
  Code,
  CopyButton,
  Group,
  Paper,
  Skeleton,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
} from "@mantine/core";
import { useForm } from "@mantine/form";
import { modals } from "@mantine/modals";
import { notifications } from "@mantine/notifications";
import { IconCheck, IconCopy } from "@tabler/icons-react";
import { useState } from "react";
import { useApiTokens, useCreateApiToken, useRevokeApiToken } from "../../api/apiTokens";
import type { ApiTokenSummary, CreatedApiToken } from "../../api/types";
import { formatAgo } from "../../lib/format";
import { useNow } from "../../lib/useNow";

const dateFormat = new Intl.DateTimeFormat(undefined, { dateStyle: "medium" });

/**
 * Read-only keys for other programs, such as Home Assistant. A token sees what its owner sees and
 * cannot change anything.
 */
export function ApiTokensPanel() {
  const [created, setCreated] = useState<CreatedApiToken | null>(null);

  return (
    <Paper className="argus-surface" p="lg" radius="sm" maw={960} mt="lg">
      <Title order={2} fz={17} mb={4}>
        API tokens
      </Title>
      <Text fz="sm" c="dimmed" mb="md">
        Let another program, such as the{" "}
        <Anchor href="https://github.com/Alli1223/argus-hass" target="_blank" fz="sm">
          Home Assistant integration
        </Anchor>
        , read what you see in Argus. Tokens can only read: they cannot change settings, alerts or systems.
      </Text>
      <Stack gap="md">
        {created ? (
          <NewToken created={created} onDone={() => setCreated(null)} />
        ) : (
          <CreateForm onCreated={setCreated} />
        )}
        <TokenTable />
      </Stack>
    </Paper>
  );
}

function CreateForm({ onCreated }: { onCreated: (token: CreatedApiToken) => void }) {
  const create = useCreateApiToken();
  const form = useForm({
    initialValues: { name: "" },
    validate: {
      name: (value: string) => {
        if (value.trim() === "") return "Name it after what will use it, such as Home Assistant.";
        return value.trim().length > 100 ? "Names can be up to 100 characters." : null;
      },
    },
  });

  const submit = form.onSubmit(({ name }) =>
    create.mutate(name.trim(), {
      onSuccess: (token) => {
        form.reset();
        onCreated(token);
      },
      onError: (error) => form.setErrors(error.fieldErrors),
    }),
  );

  return (
    <form onSubmit={submit} noValidate>
      <Group align="flex-start" gap="sm">
        <TextInput
          aria-label="Token name"
          placeholder="Home Assistant"
          style={{ flex: "1 1 260px" }}
          maw={360}
          {...form.getInputProps("name")}
        />
        <Button type="submit" loading={create.isPending}>
          Create token
        </Button>
      </Group>
    </form>
  );
}

function NewToken({ created, onDone }: { created: CreatedApiToken; onDone: () => void }) {
  return (
    <Alert color="healthy" title={`${created.summary.name} token created`}>
      <Stack gap="sm">
        <Text fz="sm">
          Copy it now. Argus shows it only once; if you lose it, revoke it and create another.
        </Text>
        <Group gap="sm" wrap="nowrap" align="center">
          <Code style={{ wordBreak: "break-all", flex: 1 }}>{created.token}</Code>
          <CopyButton value={created.token}>
            {({ copied, copy }) => (
              <Button
                size="compact-sm"
                variant={copied ? "light" : "filled"}
                color={copied ? "healthy" : undefined}
                leftSection={copied ? <IconCheck size={14} /> : <IconCopy size={14} />}
                onClick={copy}
              >
                {copied ? "Copied" : "Copy"}
              </Button>
            )}
          </CopyButton>
        </Group>
        <Button variant="default" size="compact-sm" w="fit-content" onClick={onDone}>
          Done
        </Button>
      </Stack>
    </Alert>
  );
}

function TokenTable() {
  const tokens = useApiTokens();
  const revoke = useRevokeApiToken();
  const now = useNow();

  if (tokens.isPending) return <Skeleton h={60} />;
  if (!tokens.data || tokens.data.length === 0) {
    return (
      <Text fz="sm" c="dimmed">
        No tokens yet.
      </Text>
    );
  }

  const confirmRevoke = (token: ApiTokenSummary) =>
    modals.openConfirmModal({
      title: `Revoke ${token.name}?`,
      children: <Text fz="sm">It stops working at once. Whatever uses it can no longer read Argus.</Text>,
      labels: { confirm: "Revoke token", cancel: "Keep it" },
      confirmProps: { color: "crimson" },
      onConfirm: () =>
        revoke.mutate(token.id, {
          onSuccess: () => notifications.show({ color: "healthy", message: `${token.name} revoked.` }),
          onError: (error) =>
            notifications.show({
              color: "crimson",
              title: "The token was not revoked",
              message: error.message,
            }),
        }),
    });

  return (
    <Box style={{ overflowX: "auto" }}>
      <Table miw={560}>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>Name</Table.Th>
            <Table.Th>Created</Table.Th>
            <Table.Th>Last used</Table.Th>
            <Table.Th>
              <Box component="span" style={{ position: "absolute", left: -9999 }}>
                Status
              </Box>
            </Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {tokens.data.map((token) => (
            <Table.Tr key={token.id} style={token.isActive ? undefined : { opacity: 0.62 }}>
              <Table.Td>
                <Text fz="sm" fw={600}>
                  {token.name}
                </Text>
                <Text fz="xs" c="dimmed" ff="monospace">
                  {token.tokenPrefix}
                </Text>
              </Table.Td>
              <Table.Td>{dateFormat.format(new Date(token.createdAt))}</Table.Td>
              <Table.Td>{token.lastUsedAt ? formatAgo(token.lastUsedAt, now) : "Never"}</Table.Td>
              <Table.Td align="right">
                {token.isActive ? (
                  <Button
                    variant="subtle"
                    color="crimson"
                    size="compact-sm"
                    onClick={() => confirmRevoke(token)}
                  >
                    Revoke
                  </Button>
                ) : (
                  <Badge size="sm" variant="light" color="gray">
                    Revoked
                  </Badge>
                )}
              </Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </Box>
  );
}
