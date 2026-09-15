import { useQuery } from "@tanstack/react-query";
import { api } from "./client";
import type { ServerInfo } from "./types";

/** The server's name, version and public address; they do not change while the page is open. */
export function useServerInfo() {
  return useQuery({
    queryKey: ["server", "info"],
    queryFn: () => api.get<ServerInfo>("/api/info"),
    staleTime: Infinity,
  });
}
