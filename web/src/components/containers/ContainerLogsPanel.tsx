import { Alert, Box, Button, Group, SegmentedControl, Skeleton, Switch, Text } from "@mantine/core";
import { IconAlertTriangle, IconRefresh } from "@tabler/icons-react";
import { useEffect, useRef, useState } from "react";
import { ApiError } from "../../api/client";
import { useContainerLogs } from "../../api/containers";
import { Section } from "../Section";

const TAILS = ["100", "500", "1000"];

const clock = new Intl.DateTimeFormat(undefined, { hour: "2-digit", minute: "2-digit", second: "2-digit" });

interface ContainerLogsPanelProps {
  hostId: string;
  name: string;
  actionsEnabled: boolean;
}

/** A container's newest log lines, read through its agent, optionally followed as they come. */
export function ContainerLogsPanel({ hostId, name, actionsEnabled }: ContainerLogsPanelProps) {
  const [tail, setTail] = useState("100");
  const [follow, setFollow] = useState(false);
  const logs = useContainerLogs(hostId, name, Number(tail), follow, actionsEnabled);
  const view = useRef<HTMLDivElement>(null);
  const atBottom = useRef(true);

  // New lines scroll into view, unless someone scrolled up to read.
  useEffect(() => {
    const element = view.current;
    if (element && atBottom.current) element.scrollTop = element.scrollHeight;
  }, [logs.data]);

  if (!actionsEnabled) {
    return (
      <Section title="Logs" mt="xl">
        <Text fz="sm" c="dimmed" p="md">
          Logs are off on this machine, like starting and stopping its containers. Its agent's install command
          switches them on with --container-actions.
        </Text>
      </Section>
    );
  }

  const lines = logs.data?.lines ?? [];
  return (
    <Section
      title="Logs"
      mt="xl"
      action={
        <Group gap="sm" wrap="wrap">
          <SegmentedControl
            aria-label="Lines"
            size="xs"
            value={tail}
            onChange={setTail}
            data={TAILS.map((value) => ({ value, label: `Last ${value}` }))}
          />
          <Switch
            label="Follow"
            checked={follow}
            onChange={(event) => setFollow(event.currentTarget.checked)}
          />
          <Button
            size="compact-sm"
            variant="default"
            leftSection={<IconRefresh size={14} />}
            loading={logs.isFetching && !follow}
            onClick={() => void logs.refetch()}
          >
            Refresh
          </Button>
        </Group>
      }
    >
      {logs.isError && (
        <Alert
          color="bronze"
          icon={<IconAlertTriangle size={18} />}
          m="md"
          title={logs.error instanceof ApiError ? logs.error.title : "The logs did not load"}
        >
          {logs.error.message}
        </Alert>
      )}
      {logs.isPending && !logs.isError ? (
        <Skeleton h={240} />
      ) : (
        !logs.isError && (
          <Box
            ref={view}
            role="log"
            aria-label={`${name} logs`}
            onScroll={(event) => {
              const element = event.currentTarget;
              atBottom.current = element.scrollHeight - element.scrollTop - element.clientHeight < 24;
            }}
            p="sm"
            style={{
              maxHeight: 480,
              overflow: "auto",
              fontFamily: "var(--mantine-font-family-monospace)",
              fontSize: 12,
              lineHeight: 1.6,
              whiteSpace: "pre-wrap",
              overflowWrap: "anywhere",
            }}
          >
            {logs.data?.truncated && (
              <Text fz="xs" c="dimmed" mb={4}>
                Older lines are left out.
              </Text>
            )}
            {lines.length === 0 ? (
              <Text fz="sm" c="dimmed">
                No log lines.
              </Text>
            ) : (
              lines.map((line, index) => (
                <div key={index}>
                  {line.time && (
                    <span style={{ color: "var(--mantine-color-dimmed)" }}>
                      {clock.format(new Date(line.time))}{" "}
                    </span>
                  )}
                  {line.stream === "stderr" && (
                    <span style={{ color: "var(--mantine-color-dimmed)" }}>err </span>
                  )}
                  {line.text}
                </div>
              ))
            )}
          </Box>
        )
      )}
    </Section>
  );
}
