import { Box, Group, Title, type MantineSpacing } from "@mantine/core";
import type { ReactNode } from "react";

interface SectionProps {
  title: string;
  /** A link or note at the right of the title. */
  action?: ReactNode;
  mt?: MantineSpacing;
  children: ReactNode;
}

/** A titled block of a page, its content on the panel surface. Wide tables inside scroll sideways. */
export function Section({ title, action, mt, children }: SectionProps) {
  return (
    <Box mt={mt}>
      <Group justify="space-between" mb="xs" align="baseline">
        <Title order={2} fz={17}>
          {title}
        </Title>
        {action}
      </Group>
      <Box className="argus-surface" style={{ borderRadius: "var(--mantine-radius-sm)", overflowX: "auto" }}>
        {children}
      </Box>
    </Box>
  );
}
