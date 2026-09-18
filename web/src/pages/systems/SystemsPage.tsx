import {
  Alert,
  Anchor,
  Badge,
  Box,
  Button,
  Checkbox,
  Code,
  CopyButton,
  Group,
  Loader,
  Paper,
  SegmentedControl,
  Skeleton,
  Stack,
  Table,
  TagsInput,
  Text,
  TextInput,
  ThemeIcon,
  Title,
} from "@mantine/core";
import { useForm } from "@mantine/form";
import { modals } from "@mantine/modals";
import { notifications } from "@mantine/notifications";
import { IconAlertTriangle, IconCheck, IconCircleCheck, IconCopy } from "@tabler/icons-react";
import { useId, useState, type ReactNode } from "react";
import { Link } from "react-router";
import {
  useCreateEnrollmentToken,
  useEnrollmentTokens,
  useRevokeEnrollmentToken,
} from "../../api/enrollment";
import { useServerInfo } from "../../api/server";
import type { CreatedEnrollmentToken, EnrollmentTokenSummary } from "../../api/types";
import { PageHeader } from "../../components/PageHeader";
import { Section } from "../../components/Section";
import { formatAgo } from "../../lib/format";
import { MAX_TAGS, normalizeTags, tagsError } from "../../lib/tags";
import { useNow } from "../../lib/useNow";
import {
  describeExpiry,
  installCommands,
  isInsecureAddress,
  serverAddress,
  tokenStatus,
  type TokenStatus,
} from "./install";
import classes from "./SystemsPage.module.css";

const day = new Intl.DateTimeFormat(undefined, { day: "numeric", month: "short" });
const dateTime = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" });

export function SystemsPage() {
  const [created, setCreated] = useState<CreatedEnrollmentToken | null>(null);

  return (
    <>
      <PageHeader
        title="Add a system"
        description="Install the Argus agent on a machine and it starts reporting within a minute."
      />

      <Stack component="ol" gap="md" maw={820} className={classes.steps}>
        <Step number={1} title="Create an enrollment token" done={created !== null}>
          {created ? (
            <Group gap="sm" wrap="wrap">
              <Text fz="sm">
                Created {created.summary.name}. {describeToken(created.summary)}
              </Text>
              <Button variant="subtle" size="compact-sm" onClick={() => setCreated(null)}>
                Create another
              </Button>
            </Group>
          ) : (
            <TokenForm onCreated={setCreated} />
          )}
        </Step>
        <Step number={2} title="Run the installer on the machine" disabled={created === null}>
          {created ? (
            <InstallCommands token={created.token} />
          ) : (
            <Text fz="sm" c="dimmed">
              The command appears here once you have a token.
            </Text>
          )}
        </Step>
        <Step number={3} title="Watch it arrive" disabled={created === null}>
          {created ? (
            <Arrivals tokenId={created.summary.id} />
          ) : (
            <Text fz="sm" c="dimmed">
              Argus shows here when the machine registers.
            </Text>
          )}
        </Step>
      </Stack>

      <TokensSection highlight={created?.summary.id} />
    </>
  );
}

function describeToken(token: EnrollmentTokenSummary): string {
  const machines =
    token.maxUses === 1
      ? "one machine"
      : token.maxUses
        ? `${token.maxUses} machines`
        : "any number of machines";
  const until = token.expiresAt ? `until ${dateTime.format(new Date(token.expiresAt))}` : "and never expires";
  return `It registers ${machines} ${until}.`;
}

interface StepProps {
  number: number;
  title: string;
  done?: boolean;
  disabled?: boolean;
  children: ReactNode;
}

/** One step of adding a system. The steps are a real sequence, so they are numbered. */
function Step({ number, title, done = false, disabled = false, children }: StepProps) {
  return (
    <Paper
      component="li"
      className="argus-surface"
      p="lg"
      radius="sm"
      style={{ opacity: disabled ? 0.6 : 1 }}
    >
      <Group align="flex-start" gap="md" wrap="nowrap">
        <ThemeIcon
          radius="xl"
          size={30}
          variant={done ? "filled" : "light"}
          color={done ? "healthy" : "iris"}
          aria-hidden
        >
          {done ? (
            <IconCheck size={16} />
          ) : (
            <Text fz="sm" fw={700}>
              {number}
            </Text>
          )}
        </ThemeIcon>
        <Stack gap="sm" style={{ flex: 1, minWidth: 0 }}>
          <Title order={2} fz={17}>
            {title}
          </Title>
          {children}
        </Stack>
      </Group>
    </Paper>
  );
}

interface TokenValues {
  name: string;
  tags: string[];
  expires: string;
  machines: string;
}

const EXPIRY_CHOICES = [
  { label: "1 hour", value: "1" },
  { label: "1 day", value: "24" },
  { label: "7 days", value: "168" },
  { label: "Always", value: "never" },
];

const MACHINE_CHOICES = [
  { label: "Any number", value: "any" },
  { label: "Just one", value: "one" },
];

function TokenForm({ onCreated }: { onCreated: (token: CreatedEnrollmentToken) => void }) {
  const now = useNow();
  const ids = useId();
  const create = useCreateEnrollmentToken();
  const form = useForm<TokenValues>({
    initialValues: { name: `Added ${day.format(new Date(now))}`, tags: [], expires: "24", machines: "any" },
    validate: {
      name: (value) => {
        if (value.trim() === "") return "Name the token, so you can tell it apart later.";
        return value.trim().length > 100 ? "Names can be up to 100 characters." : null;
      },
      tags: tagsError,
    },
  });

  const submit = form.onSubmit((values) =>
    create.mutate(
      {
        name: values.name.trim(),
        tags: values.tags,
        expiresInHours: values.expires === "never" ? null : Number(values.expires),
        maxUses: values.machines === "one" ? 1 : null,
      },
      { onSuccess: onCreated, onError: (error) => form.setErrors(error.fieldErrors) },
    ),
  );

  return (
    <form onSubmit={submit} noValidate>
      <Stack gap="md">
        <TextInput
          label="Name"
          description="For telling tokens apart later."
          required
          {...form.getInputProps("name")}
        />
        <TagsInput
          label="Tags"
          description="Machines that register with this token get these tags."
          placeholder="Add a tag"
          splitChars={[",", " "]}
          maxTags={MAX_TAGS}
          clearable
          {...form.getInputProps("tags")}
          onChange={(tags) => form.setFieldValue("tags", normalizeTags(tags))}
        />
        <Group gap="xl" wrap="wrap" align="flex-start">
          <Stack gap={4}>
            <Text id={`${ids}-expires`} fz="sm" fw={500}>
              Works for
            </Text>
            <SegmentedControl
              aria-labelledby={`${ids}-expires`}
              data={EXPIRY_CHOICES}
              {...form.getInputProps("expires")}
            />
          </Stack>
          <Stack gap={4}>
            <Text id={`${ids}-machines`} fz="sm" fw={500}>
              Machines
            </Text>
            <SegmentedControl
              aria-labelledby={`${ids}-machines`}
              data={MACHINE_CHOICES}
              {...form.getInputProps("machines")}
            />
          </Stack>
        </Group>
        {create.error && Object.keys(create.error.fieldErrors).length === 0 && (
          <Alert color="crimson" title={create.error.title}>
            {create.error.message}
          </Alert>
        )}
        <Button type="submit" loading={create.isPending} w="fit-content">
          Create token
        </Button>
      </Stack>
    </form>
  );
}

function CommandBlock({ command }: { command: string }) {
  return (
    <Box pos="relative">
      <Code block className={classes.command}>
        {command}
      </Code>
      <CopyButton value={command}>
        {({ copied, copy }) => (
          <Button
            className={classes.copy}
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
    </Box>
  );
}

function InstallCommands({ token }: { token: string }) {
  const info = useServerInfo();
  const [platform, setPlatform] = useState<"linux" | "windows" | "docker">("linux");
  const [docker, setDocker] = useState(false);
  const [containerActions, setContainerActions] = useState(false);

  if (info.isPending) return <Skeleton h={96} />;

  const server = serverAddress(info.data?.publicUrl, window.location.origin);
  const commands = installCommands(server, token, { docker, containerActions });
  return (
    <Stack gap="sm">
      <SegmentedControl
        aria-label="Operating system"
        w="fit-content"
        value={platform}
        onChange={(value) => setPlatform(value as "linux" | "windows" | "docker")}
        data={[
          { label: "Linux", value: "linux" },
          { label: "Windows", value: "windows" },
          { label: "Container", value: "docker" },
        ]}
      />
      <Text fz="sm">
        {platform === "linux" &&
          "Run this on the machine. It needs sudo, systemd and a 64-bit x86 or ARM processor."}
        {platform === "windows" && "Run this in PowerShell opened as administrator, on 64-bit Windows."}
        {platform === "docker" &&
          "For machines with Docker but no systemd, such as a NAS. The agent watches the machine from a container, which Docker restarts with the machine."}
      </Text>
      {platform !== "windows" && (
        <Stack gap="xs">
          <Checkbox
            checked={docker || containerActions}
            disabled={containerActions}
            onChange={(event) => setDocker(event.currentTarget.checked)}
            label="Watch Docker containers"
            description={
              platform === "docker"
                ? "The container is given Docker's socket, which lets it do anything root could on the machine."
                : "The agent joins the docker group to read Docker, which lets it do anything root could on the machine."
            }
          />
          <Checkbox
            checked={containerActions}
            onChange={(event) => setContainerActions(event.currentTarget.checked)}
            label="Allow container logs and start, stop and restart"
            description="Anyone who can see this machine in Argus can then read its containers' logs and stop them."
          />
        </Stack>
      )}
      <CommandBlock command={commands[platform]} />
      <Text fz="xs" c="dimmed">
        The command contains the token, which Argus shows only once. Copy it before you leave this page.
      </Text>
      {isInsecureAddress(server) && (
        <Alert
          color="bronze"
          icon={<IconAlertTriangle size={18} />}
          title="Agents will connect over plain HTTP"
        >
          They reach Argus at {server}, so their keys travel unencrypted. Serve Argus over HTTPS before adding
          machines outside a network you trust.
        </Alert>
      )}
    </Stack>
  );
}

function Arrivals({ tokenId }: { tokenId: string }) {
  const tokens = useEnrollmentTokens(5_000);
  const count = tokens.data?.find((token) => token.id === tokenId)?.useCount ?? 0;

  if (count === 0) {
    return (
      <Group gap="xs">
        <Loader size="xs" />
        <Text fz="sm">Waiting for the machine to register…</Text>
      </Group>
    );
  }

  return (
    <Group gap="xs">
      <IconCircleCheck size={18} color="var(--mantine-color-healthy-filled)" aria-hidden />
      <Text fz="sm">
        {count === 1
          ? "A machine has registered with this token."
          : `${count} machines have registered with this token.`}
      </Text>
      <Anchor component={Link} to="/hosts" fz="sm">
        See your hosts
      </Anchor>
    </Group>
  );
}

const statusColor: Record<TokenStatus, string> = {
  Active: "healthy",
  Revoked: "gray",
  Expired: "gray",
  "Used up": "gray",
};

function TokensSection({ highlight }: { highlight?: string }) {
  const tokens = useEnrollmentTokens();
  const revoke = useRevokeEnrollmentToken();
  const now = useNow();

  const confirmRevoke = (token: EnrollmentTokenSummary) =>
    modals.openConfirmModal({
      title: `Revoke ${token.name}?`,
      children: (
        <Text fz="sm">
          No more machines can register with it. Machines that already registered keep reporting.
        </Text>
      ),
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
    <Section title="Enrollment tokens" mt="xl">
      {tokens.isPending ? (
        <Skeleton h={100} />
      ) : !tokens.data || tokens.data.length === 0 ? (
        <Text fz="sm" c="dimmed" p="md">
          No tokens yet. The first one you create appears here.
        </Text>
      ) : (
        <Table miw={820}>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Name</Table.Th>
              <Table.Th>Tags</Table.Th>
              <Table.Th>Machines</Table.Th>
              <Table.Th>Expires</Table.Th>
              <Table.Th>Last used</Table.Th>
              <Table.Th>Status</Table.Th>
              <Table.Th>
                <Box component="span" style={{ position: "absolute", left: -9999 }}>
                  Actions
                </Box>
              </Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {tokens.data.map((token) => {
              const status = tokenStatus(token, now);
              return (
                <Table.Tr key={token.id} className={token.id === highlight ? classes.highlight : undefined}>
                  <Table.Td>
                    <Text fz="sm" fw={600}>
                      {token.name}
                    </Text>
                    <Text fz="xs" c="dimmed" ff="monospace">
                      {token.tokenPrefix}…
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    {token.tags.length > 0 ? (
                      <Group gap={4}>
                        {token.tags.map((tag) => (
                          <Badge key={tag} size="xs" variant="outline" color="gray">
                            {tag}
                          </Badge>
                        ))}
                      </Group>
                    ) : (
                      <Text fz="sm" c="dimmed">
                        None
                      </Text>
                    )}
                  </Table.Td>
                  <Table.Td>
                    {token.maxUses ? `${token.useCount} of ${token.maxUses}` : `${token.useCount}, no limit`}
                  </Table.Td>
                  <Table.Td>{describeExpiry(token.expiresAt, now)}</Table.Td>
                  <Table.Td>{token.lastUsedAt ? formatAgo(token.lastUsedAt, now) : "Never"}</Table.Td>
                  <Table.Td>
                    <Group gap={6} wrap="nowrap">
                      <Box
                        w={8}
                        h={8}
                        style={{
                          borderRadius: 4,
                          background: `var(--mantine-color-${statusColor[status]}-filled)`,
                        }}
                        aria-hidden
                      />
                      {status}
                    </Group>
                  </Table.Td>
                  <Table.Td ta="right">
                    {status === "Active" && (
                      <Button
                        variant="subtle"
                        color="crimson"
                        size="compact-sm"
                        onClick={() => confirmRevoke(token)}
                        aria-label={`Revoke ${token.name}`}
                      >
                        Revoke
                      </Button>
                    )}
                  </Table.Td>
                </Table.Tr>
              );
            })}
          </Table.Tbody>
        </Table>
      )}
    </Section>
  );
}
