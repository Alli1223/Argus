import { Group, Text } from "@mantine/core";
import { IconAlertTriangle, IconInfoCircle, IconUrgent } from "@tabler/icons-react";
import type { AlertSeverity } from "../api/types";
import { SEVERITY_COLORS } from "../lib/alertMetrics";

const icons: Record<AlertSeverity, typeof IconUrgent> = {
  Critical: IconUrgent,
  Warning: IconAlertTriangle,
  Info: IconInfoCircle,
};

/** Severity as an icon and a word in the severity's colour, never colour alone. */
export function SeverityLabel({ severity }: { severity: AlertSeverity }) {
  const Icon = icons[severity];
  const color = `var(--mantine-color-${SEVERITY_COLORS[severity]}-text)`;
  return (
    <Group gap={4} wrap="nowrap" c={color}>
      <Icon size={15} aria-hidden />
      <Text fz="sm" fw={600} c={color}>
        {severity}
      </Text>
    </Group>
  );
}
