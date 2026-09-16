import {
  Anchor,
  Badge,
  Button,
  Code,
  Group,
  Modal,
  ScrollArea,
  Stack,
  Text,
  UnstyledButton,
} from "@mantine/core";
import { useDisclosure } from "@mantine/hooks";
import { IconArrowUpCircle } from "@tabler/icons-react";
import { useCurrentUser } from "../api/auth";
import type { ServerUpdateInfo } from "../api/types";
import { useCheckForUpdates, useServerUpdate } from "../api/updates";
import { formatAgo } from "../lib/format";
import { serverUpgradeCommands } from "../lib/updates";
import { useNow } from "../lib/useNow";

const dateFormat = new Intl.DateTimeFormat(undefined, { dateStyle: "medium" });

/** For administrators: a badge in the header when a newer Argus is out, opening what it brings and how to install it. */
export function ServerUpdateBadge() {
  const me = useCurrentUser();
  const update = useServerUpdate(me.data?.isAdmin === true);
  const [opened, modal] = useDisclosure(false);

  if (!update.data?.updateAvailable || !update.data.latest) return null;

  return (
    <>
      <UnstyledButton onClick={modal.open} aria-label={`Argus ${update.data.latest.version} is available`}>
        <Badge
          color="iris"
          variant="light"
          leftSection={<IconArrowUpCircle size={13} />}
          style={{ cursor: "pointer" }}
        >
          Argus {update.data.latest.version}
        </Badge>
      </UnstyledButton>
      <ServerUpdateModal info={update.data} opened={opened} onClose={modal.close} />
    </>
  );
}

interface ServerUpdateModalProps {
  info: ServerUpdateInfo;
  opened: boolean;
  onClose: () => void;
}

/** This server's version, the latest release's notes and the commands that install it. */
export function ServerUpdateModal({ info, opened, onClose }: ServerUpdateModalProps) {
  const check = useCheckForUpdates();
  const now = useNow();
  const latest = info.latest;

  return (
    <Modal opened={opened} onClose={onClose} title="Argus updates" size="lg">
      <Stack gap="md">
        {latest && info.updateAvailable ? (
          <Text>
            Argus <strong>{latest.version}</strong> came out on{" "}
            {dateFormat.format(new Date(latest.publishedAt))}. This server runs {info.currentVersion}.
          </Text>
        ) : (
          <Text>This server runs Argus {info.currentVersion}, the latest release.</Text>
        )}

        {latest && info.updateAvailable && (
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
            <Stack gap={4}>
              <Text fz="sm" fw={500}>
                To install it, run these where Argus was installed:
              </Text>
              <Code block>{serverUpgradeCommands(latest.tag)}</Code>
              <Text fz="xs" c="dimmed">
                Take a backup first. Once the server runs {latest.version}, update the agents from the Hosts
                page.
              </Text>
            </Stack>
          </>
        )}

        <Group justify="space-between" gap="sm" wrap="wrap">
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
      </Stack>
    </Modal>
  );
}
