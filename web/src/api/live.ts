import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { notifications } from "@mantine/notifications";
import { useQueryClient, type QueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { SEVERITY_COLORS } from "../lib/alertMetrics";
import { alertKeys } from "./alerts";
import { dashboardKeys, hostKeys } from "./hosts";
import {
  isLiveRange,
  withMetrics,
  withStatus,
  type LiveAlert,
  type LiveHostMetrics,
  type LiveHostStatus,
} from "./liveCache";
import type { HostDetail, HostSummary } from "./types";

export type LiveState = "connecting" | "live" | "reconnecting" | "offline";

const HUB_PATH = "/hubs/live";
/** How long to wait before trying again once the connection is lost for good or never came up. */
const RETRY_MS = 15_000;
/** The fleet summary is a heavier query; readings from many hosts refresh it at most this often. */
const SUMMARY_EVERY_MS = 10_000;

let summaryRefreshedAt = 0;

function refreshSummary(client: QueryClient) {
  const now = Date.now();
  if (now - summaryRefreshedAt < SUMMARY_EVERY_MS) return;
  summaryRefreshedAt = now;
  void client.invalidateQueries({ queryKey: dashboardKeys.summary });
}

function applyMetrics(client: QueryClient, update: LiveHostMetrics) {
  const receivedAt = new Date().toISOString();
  client.setQueryData<HostSummary[]>(hostKeys.list, (hosts) =>
    hosts?.map((host) => withMetrics(host, update, receivedAt)),
  );
  client.setQueryData<HostDetail>(hostKeys.detail(update.hostId), (host) =>
    host ? withMetrics(host, update, receivedAt) : host,
  );
  // Charts of this host redraw if they are on screen and follow the present; zoomed windows stay put.
  void client.invalidateQueries({
    predicate: ({ queryKey }) =>
      queryKey[0] === "hosts" &&
      queryKey[1] === "metrics" &&
      queryKey[2] === update.hostId &&
      isLiveRange(queryKey[3]),
  });
  refreshSummary(client);
}

function applyStatus(client: QueryClient, update: LiveHostStatus) {
  client.setQueryData<HostSummary[]>(hostKeys.list, (hosts) =>
    hosts?.map((host) => withStatus(host, update)),
  );
  client.setQueryData<HostDetail>(hostKeys.detail(update.hostId), (host) =>
    host ? withStatus(host, update) : host,
  );
  void client.invalidateQueries({ queryKey: dashboardKeys.summary });
}

function applyAlert(client: QueryClient, update: LiveAlert) {
  void client.invalidateQueries({ queryKey: alertKeys.all });
  void client.invalidateQueries({ queryKey: alertKeys.rules });
  void client.invalidateQueries({ queryKey: dashboardKeys.summary });
  notifications.show(
    update.kind === "Fired"
      ? { color: SEVERITY_COLORS[update.severity], title: `${update.severity} alert`, message: update.title }
      : { color: "healthy", title: "Resolved", message: update.title },
  );
}

/**
 * Keeps cached hosts and alerts current with what the server pushes, for as long as the caller is
 * mounted. The pages' own polling carries on underneath, so losing the connection only slows things.
 */
export function useLiveUpdates(): LiveState {
  const client = useQueryClient();
  const [state, setState] = useState<LiveState>("connecting");

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(HUB_PATH)
      .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000])
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on("HostMetrics", (update: LiveHostMetrics) => applyMetrics(client, update));
    connection.on("HostStatus", (update: LiveHostStatus) => applyStatus(client, update));
    connection.on("AlertChanged", (update: LiveAlert) => applyAlert(client, update));

    let stopped = false;
    let connectedBefore = false;
    let retry: ReturnType<typeof setTimeout> | undefined;

    // Anything could have changed while the connection was down.
    const catchUp = () => void client.invalidateQueries();

    const retryLater = () => {
      if (stopped) return;
      setState("offline");
      retry = setTimeout(connect, RETRY_MS);
    };

    function connect() {
      connection.start().then(() => {
        if (stopped) return;
        setState("live");
        if (connectedBefore) catchUp();
        connectedBefore = true;
      }, retryLater);
    }

    connection.onreconnecting(() => setState("reconnecting"));
    connection.onreconnected(() => {
      setState("live");
      catchUp();
    });
    connection.onclose(retryLater);
    connect();

    return () => {
      stopped = true;
      clearTimeout(retry);
      void connection.stop();
    };
  }, [client]);

  return state;
}
