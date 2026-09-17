import { Button, Group, Text } from "@mantine/core";
import { modals } from "@mantine/modals";
import { notifications } from "@mantine/notifications";
import { IconPlayerPlay, IconPlayerStop, IconRefresh } from "@tabler/icons-react";
import { useContainerAction, type ContainerAction } from "../../api/containers";
import type { ContainerSummary } from "../../api/types";
import { isUp } from "../../lib/containers";

interface ContainerActionsProps {
  hostId: string;
  hostName: string;
  container: ContainerSummary;
  actionsEnabled: boolean;
}

const DONE: Record<ContainerAction, string> = { start: "started", stop: "stopped", restart: "restarted" };

/** Start, stop and restart, on machines that allow them; stopping and restarting ask first. */
export function ContainerActions({ hostId, hostName, container, actionsEnabled }: ContainerActionsProps) {
  const action = useContainerAction(hostId, container.name);

  if (!actionsEnabled) {
    return (
      <Text fz="xs" c="dimmed" maw={320}>
        Logs and starting, stopping and restarting are off on this machine. Its agent's install command
        switches them on with --container-actions.
      </Text>
    );
  }

  const run = (kind: ContainerAction) =>
    action.mutate(kind, {
      onSuccess: () => notifications.show({ color: "healthy", message: `${container.name} ${DONE[kind]}.` }),
      onError: (error) =>
        notifications.show({
          color: "crimson",
          title: `${container.name} was not ${DONE[kind]}`,
          message: error.message,
        }),
    });

  const confirm = (kind: "stop" | "restart") =>
    modals.openConfirmModal({
      title: `${kind === "stop" ? "Stop" : "Restart"} ${container.name}?`,
      children: (
        <Text fz="sm">
          {kind === "stop"
            ? `Docker asks it to stop and, after 10 seconds, kills it. It stays stopped on ${hostName} until someone starts it`
            : `Docker stops it, giving it 10 seconds, and starts it again on ${hostName}`}
          {container.restartPolicy === "always" && kind === "stop"
            ? ", although its restart policy starts it again when Docker restarts."
            : "."}
        </Text>
      ),
      labels: { confirm: kind === "stop" ? "Stop container" : "Restart container", cancel: "Cancel" },
      confirmProps: { color: kind === "stop" ? "crimson" : undefined },
      onConfirm: () => run(kind),
    });

  const busy = action.isPending ? action.variables : undefined;
  return (
    <Group gap="xs">
      {isUp(container) ? (
        <>
          <Button
            variant="default"
            leftSection={<IconRefresh size={16} />}
            loading={busy === "restart"}
            disabled={action.isPending}
            onClick={() => confirm("restart")}
          >
            Restart
          </Button>
          <Button
            variant="default"
            color="crimson"
            leftSection={<IconPlayerStop size={16} />}
            loading={busy === "stop"}
            disabled={action.isPending}
            onClick={() => confirm("stop")}
          >
            Stop
          </Button>
        </>
      ) : (
        <Button
          leftSection={<IconPlayerPlay size={16} />}
          loading={busy === "start"}
          disabled={action.isPending}
          onClick={() => run("start")}
        >
          Start
        </Button>
      )}
    </Group>
  );
}
