import { Group, Text } from "@mantine/core";

/** The eye of a peacock feather: bronze rim, green iris, blue-violet pupil. */
export function EyeMark({ size = 26 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 32 32" aria-hidden="true" focusable="false">
      <path
        d="M3.5 16C8 9 12 7 16 7s8 2 12.5 9C24 23 20 25 16 25s-8-2-12.5-9Z"
        fill="none"
        stroke="var(--mantine-color-bronze-5)"
        strokeWidth="1.8"
      />
      <circle cx="16" cy="16" r="6.2" fill="var(--mantine-color-healthy-6)" />
      <circle cx="16" cy="16" r="3.1" fill="var(--mantine-color-iris-4)" />
    </svg>
  );
}

export function Wordmark({ size = "sm" }: { size?: "sm" | "lg" }) {
  const large = size === "lg";
  return (
    <Group gap={large ? 10 : 8} wrap="nowrap" align="center">
      <EyeMark size={large ? 40 : 26} />
      <Text
        component="span"
        className="argus-wide"
        fw={700}
        fz={large ? 30 : 19}
        lh={1}
        c="var(--mantine-color-text)"
      >
        Argus
      </Text>
    </Group>
  );
}
