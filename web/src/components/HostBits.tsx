import { Box, Group, Text } from "@mantine/core";
import { IconBrandWindows, IconDeviceDesktop, IconTerminal2 } from "@tabler/icons-react";
import type { HostPlatform, HostStatus } from "../api/types";

/** Online or offline, as a dot and a word (never colour alone). */
export function HostStatusBadge({ status }: { status: HostStatus }) {
  const online = status === "Online";
  return (
    <Group gap={6} wrap="nowrap">
      <Box
        w={8}
        h={8}
        aria-hidden
        style={{
          flexShrink: 0,
          borderRadius: "50%",
          background: online ? "var(--mantine-color-healthy-filled)" : "var(--mantine-color-gray-5)",
        }}
      />
      <Text fz="sm">{online ? "Online" : "Offline"}</Text>
    </Group>
  );
}

export function PlatformIcon({ platform, size = 16 }: { platform: HostPlatform; size?: number }) {
  const Icon =
    platform === "Windows" ? IconBrandWindows : platform === "Linux" ? IconTerminal2 : IconDeviceDesktop;
  return <Icon size={size} stroke={1.6} role="img" aria-label={platform} style={{ flexShrink: 0 }} />;
}
