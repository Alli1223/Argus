import { Button, Group, Loader, Stack, Text } from "@mantine/core";
import { notifications } from "@mantine/notifications";
import type { HostDetail } from "../api/types";
import { useCancelAgentUpdate, useRequestAgentUpdate } from "../api/updates";
import { agentUpdateState } from "../lib/updates";

/** The host's agent version, with a way to update it when a newer release is out. */
export function AgentVersion({ host, now }: { host: HostDetail; now: number }) {
  const request = useRequestAgentUpdate(host.id);
  const cancel = useCancelAgentUpdate(host.id);
  const state = agentUpdateState(host.agentUpdate, now);

  const update = () =>
    request.mutate(undefined, {
      onError: (error) =>
        notifications.show({
          color: "crimson",
          title: "The agent was not asked to update",
          message: error.message,
        }),
    });
  const withdraw = () =>
    cancel.mutate(undefined, {
      onError: (error) =>
        notifications.show({ color: "crimson", title: "Nothing changed", message: error.message }),
    });

  return (
    <Stack gap={4} align="flex-start">
      <span className="argus-data">{host.agentVersion}</span>

      {state.kind === "available" && (
        <Button size="compact-xs" variant="light" loading={request.isPending} onClick={update}>
          Update to {state.version}
        </Button>
      )}

      {state.kind === "updating" && (
        <>
          <Group gap={6} wrap="nowrap">
            <Loader size={12} aria-hidden />
            <Text fz="xs">Updating to {state.version}</Text>
            <Button
              size="compact-xs"
              variant="subtle"
              color="gray"
              loading={cancel.isPending}
              onClick={withdraw}
            >
              Cancel
            </Button>
          </Group>
          {state.stalled && (
            <Text fz="xs" c="dimmed" maw={320}>
              The agent has not installed it yet. Is the host online? Agents installed before Argus 0.2.0 need
              their install command run once more before they can update.
            </Text>
          )}
        </>
      )}

      {state.kind === "failed" && (
        <>
          <Text fz="xs" c="crimson" maw={320}>
            The last update failed: {state.error}
          </Text>
          <Group gap={4}>
            {state.available && (
              <Button size="compact-xs" variant="light" loading={request.isPending} onClick={update}>
                Try again
              </Button>
            )}
            <Button
              size="compact-xs"
              variant="subtle"
              color="gray"
              loading={cancel.isPending}
              onClick={withdraw}
            >
              Dismiss
            </Button>
          </Group>
        </>
      )}
    </Stack>
  );
}
