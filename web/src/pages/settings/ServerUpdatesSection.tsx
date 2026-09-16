import {
  Alert,
  Anchor,
  Box,
  Button,
  Code,
  Group,
  Loader,
  ScrollArea,
  Stack,
  Text,
  Title,
  VisuallyHidden,
} from "@mantine/core";
import { modals } from "@mantine/modals";
import { notifications } from "@mantine/notifications";
import {
  IconAlertTriangle,
  IconArrowBackUp,
  IconCircleCheck,
  IconCircleDashed,
  IconCircleX,
} from "@tabler/icons-react";
import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { useServerInfo } from "../../api/server";
import type { ServerSelfUpdate, ServerUpdateInfo, ServerUpdateRun } from "../../api/types";
import {
  updateKeys,
  useCheckForUpdates,
  useServerSelfUpdate,
  useServerUpdate,
  useStartServerUpdate,
} from "../../api/updates";
import { Section } from "../../components/Section";
import { formatAgo } from "../../lib/format";
import { serverUpdateSteps, serverUpgradeCommands, type ServerUpdateStep } from "../../lib/updates";
import { useNow } from "../../lib/useNow";

const dateFormat = new Intl.DateTimeFormat(undefined, { dateStyle: "medium" });
const dateTimeFormat = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" });

/**
 * This server's version against the latest release, and installing that release: from here when the
 * updater service runs, by hand otherwise.
 */
export function ServerUpdatesSection() {
  const update = useServerUpdate(true);
  const self = useServerSelfUpdate();
  const client = useQueryClient();
  const run = self.data?.lastRun ?? null;

  // When an update ends the server may run another version, so what it said about releases is stale.
  const runId = run?.id;
  const runEnded = run ? !run.inProgress : false;
  useEffect(() => {
    if (runId && runEnded) void client.invalidateQueries({ queryKey: updateKeys.server });
  }, [client, runId, runEnded]);

  return (
    <Section title="Updates">
      <Stack p="md" gap="md">
        {update.data ? (
          <Release info={update.data} self={self.data} restarting={self.isError} />
        ) : (
          <Text fz="sm" c="dimmed">
            {update.isError ? `Update information did not load: ${update.error.message}` : "Loading…"}
          </Text>
        )}
        {run && !run.inProgress && <LastRun run={run} />}
      </Stack>
    </Section>
  );
}

interface ReleaseProps {
  info: ServerUpdateInfo;
  self: ServerSelfUpdate | undefined;
  /** The server did not answer the last progress check, as happens while it restarts. */
  restarting: boolean;
}

function Release({ info, self, restarting }: ReleaseProps) {
  const check = useCheckForUpdates();
  const start = useStartServerUpdate();
  const now = useNow();
  const latest = info.latest;
  const run = self?.lastRun ?? null;
  const pending = self?.pendingVersion ?? null;
  const busy = pending !== null || run?.inProgress === true;

  const confirm = (version: string) =>
    modals.openConfirmModal({
      title: `Update Argus to ${version}?`,
      children: (
        <Stack gap="xs">
          <Text fz="sm">
            Argus backs up its database, then restarts as {version}. That takes a minute or two, during which
            the web app is unavailable; agents keep their readings and send them afterwards.
          </Text>
          <Text fz="sm">
            If {version} does not start properly, Argus goes back to {info.currentVersion} by itself, and
            restores the backup if the new version had already changed the database.
          </Text>
        </Stack>
      ),
      labels: { confirm: `Update to ${version}`, cancel: "Not now" },
      onConfirm: () =>
        start.mutate(version, {
          onError: (error) =>
            notifications.show({
              color: "crimson",
              title: "The update did not start",
              message: error.message,
            }),
        }),
    });

  return (
    <Stack gap="md">
      <Group justify="space-between" gap="sm" wrap="wrap" align="flex-start">
        <Text>
          {latest && info.updateAvailable ? (
            <>
              Argus <strong>{latest.version}</strong> came out on{" "}
              {dateFormat.format(new Date(latest.publishedAt))}. This server runs {info.currentVersion}.
            </>
          ) : (
            `This server runs Argus ${info.currentVersion}, the latest release.`
          )}
        </Text>
        <Group gap="sm" wrap="nowrap">
          <Text fz="xs" c={info.error ? "crimson" : "dimmed"}>
            {info.error
              ? `The last check failed: ${info.error}`
              : info.checkedAt
                ? `Checked ${formatAgo(info.checkedAt, now)}`
                : info.enabled
                  ? "Not checked yet"
                  : "Update checks are switched off on this server"}
          </Text>
          {info.enabled && (
            <Button
              size="compact-sm"
              variant="default"
              loading={check.isPending}
              onClick={() => check.mutate()}
            >
              Check now
            </Button>
          )}
        </Group>
      </Group>

      {busy ? (
        <Progress
          run={run?.inProgress ? run : null}
          pending={pending}
          from={info.currentVersion}
          restarting={restarting}
        />
      ) : (
        latest &&
        info.updateAvailable && (
          <>
            {latest.notes.trim() && (
              <ScrollArea.Autosize mah={220} type="auto">
                <Text fz="sm" style={{ whiteSpace: "pre-wrap" }}>
                  {latest.notes.trim()}
                </Text>
              </ScrollArea.Autosize>
            )}
            <Anchor href={latest.url} target="_blank" rel="noreferrer" fz="sm">
              The release on GitHub
            </Anchor>
            {self?.available ? (
              <Group gap="sm">
                <Button onClick={() => confirm(latest.version)} loading={start.isPending}>
                  Update to {latest.version}
                </Button>
                <Text fz="xs" c="dimmed">
                  Backs up the database first, and goes back if the new version does not start.
                </Text>
              </Group>
            ) : (
              <Stack gap={4}>
                {self?.unavailable && (
                  <Text fz="sm">Argus cannot install it by itself: {self.unavailable}</Text>
                )}
                <Text fz="sm" fw={500}>
                  To install it by hand, run these where Argus is installed:
                </Text>
                <Code block>{serverUpgradeCommands(latest.tag, latest.version)}</Code>
                <Text fz="xs" c="dimmed">
                  Take a backup first. Once the server runs {latest.version}, update the agents from the Hosts
                  page.
                </Text>
              </Stack>
            )}
          </>
        )
      )}
    </Stack>
  );
}

const STEP_ICONS = {
  done: <IconCircleCheck size={18} color="var(--mantine-color-healthy-filled)" aria-hidden />,
  current: <Loader size={16} aria-hidden />,
  waiting: <IconCircleDashed size={18} color="var(--mantine-color-dimmed)" aria-hidden />,
  failed: <IconCircleX size={18} color="var(--mantine-color-crimson-text)" aria-hidden />,
} satisfies Record<ServerUpdateStep["status"], unknown>;

const STEP_WORDS: Record<ServerUpdateStep["status"], string> = {
  done: "done",
  current: "in progress",
  waiting: "to do",
  failed: "failed",
};

interface ProgressProps {
  run: ServerUpdateRun | null;
  /** Asked for, not yet started. */
  pending: string | null;
  from: string;
  restarting: boolean;
}

/** The update under way, step by step. */
function Progress({ run, pending, from, restarting }: ProgressProps) {
  const to = run?.to ?? pending ?? "";
  const steps = serverUpdateSteps(run ?? { state: "starting", from, to });

  return (
    <Box>
      <Title order={3} fz={15} mb="xs">
        Updating to Argus {to}
      </Title>
      <Stack component="ol" gap={6} m={0} p={0} style={{ listStyle: "none" }} aria-label="Update steps">
        {steps.map((step) => (
          <Group key={step.key} component="li" gap="xs" wrap="nowrap">
            {STEP_ICONS[step.status]}
            <Text fz="sm" c={step.status === "waiting" ? "dimmed" : undefined}>
              {step.label}
            </Text>
            <VisuallyHidden>({STEP_WORDS[step.status]})</VisuallyHidden>
          </Group>
        ))}
      </Stack>
      <Text fz="xs" c="dimmed" mt="sm">
        {restarting
          ? "Argus is restarting, so it cannot answer for a moment. This page keeps checking."
          : run === null
            ? "Waiting for the updater to start."
            : "You can leave this page; the update carries on without it."}
      </Text>
    </Box>
  );
}

/** How the latest update ended, with what to do about it when it did not work. */
function LastRun({ run }: { run: ServerUpdateRun }) {
  const server = useServerInfo();
  const when = dateTimeFormat.format(new Date(run.finishedAt ?? run.startedAt));
  const by = run.requestedBy ? ` by ${run.requestedBy}` : "";

  if (run.state === "succeeded") {
    // This page was loaded from the previous version, which the browser still runs until it reloads.
    const stale = server.data !== undefined && server.data.version !== run.to;
    return (
      <Alert color="healthy" icon={<IconCircleCheck size={18} />} title={`Updated to Argus ${run.to}`}>
        <Group justify="space-between" gap="sm" wrap="wrap">
          <Text fz="sm">
            From {run.from}, on {when}
            {by}.
          </Text>
          {stale && (
            <Button size="compact-sm" onClick={() => window.location.reload()}>
              Reload to use {run.to}
            </Button>
          )}
        </Group>
      </Alert>
    );
  }

  const rolledBack = run.state === "rolled-back";
  return (
    <Alert
      color={rolledBack ? "bronze" : "crimson"}
      icon={rolledBack ? <IconArrowBackUp size={18} /> : <IconAlertTriangle size={18} />}
      title={
        rolledBack
          ? `The update to ${run.to} did not work, so Argus went back to ${run.from}`
          : `The update to ${run.to} failed`
      }
    >
      <Stack gap={6}>
        <Text fz="sm">{run.error ?? "The updater did not say why."}</Text>
        <Text fz="xs" c="dimmed">
          Started {when}
          {by}.{run.backup && ` The backup from before it is ${run.backup} on the server.`}
        </Text>
        {run.serverLog && <ServerLog log={run.serverLog} />}
      </Stack>
    </Alert>
  );
}

/** The last lines the new version logged before Argus went back, hidden until asked for. */
function ServerLog({ log }: { log: string }) {
  const [shown, setShown] = useState(false);
  return (
    <Box>
      <Button variant="subtle" size="compact-xs" px={0} onClick={() => setShown(!shown)}>
        {shown ? "Hide the log" : "Show the new version's log"}
      </Button>
      {shown && (
        <Code block fz="xs" mt={4}>
          {log}
        </Code>
      )}
    </Box>
  );
}
