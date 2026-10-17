import {
  Alert,
  Badge,
  Box,
  Button,
  Code,
  CopyButton,
  Group,
  Modal,
  Paper,
  PasswordInput,
  SimpleGrid,
  Skeleton,
  Stack,
  Text,
  TextInput,
  Title,
} from "@mantine/core";
import { notifications } from "@mantine/notifications";
import { QRCodeSVG } from "qrcode.react";
import { useState } from "react";
import {
  useDisableTwoFactor,
  useEnableTwoFactor,
  useRegenerateRecoveryCodes,
  useStartTwoFactorSetup,
  useTwoFactorStatus,
} from "../../api/account";
import type { ApiError } from "../../api/client";
import type { TwoFactorSetup } from "../../api/types";

/** Two-factor sign-in with an authenticator app: set it up, replace the recovery codes, or turn it off. */
export function TwoFactorPanel() {
  const status = useTwoFactorStatus();
  const start = useStartTwoFactorSetup();
  const [setup, setSetup] = useState<TwoFactorSetup | null>(null);
  const [codes, setCodes] = useState<string[] | null>(null);
  const [asking, setAsking] = useState<"disable" | "codes" | null>(null);

  const begin = () =>
    start.mutate(undefined, {
      onSuccess: setSetup,
      onError: (error) =>
        notifications.show({ color: "crimson", title: "Setup did not start", message: error.message }),
    });

  return (
    <Paper className="argus-surface" p="lg" radius="sm" mt="lg" maw={960}>
      <Group justify="space-between" mb="md">
        <Title order={2} fz={17}>
          Two-factor sign-in
        </Title>
        {status.data && (
          <Badge color={status.data.enabled ? "healthy" : "gray"} variant="light">
            {status.data.enabled ? "On" : "Off"}
          </Badge>
        )}
      </Group>

      {!status.data ? (
        <Skeleton h={60} />
      ) : codes ? (
        <RecoveryCodes codes={codes} onDone={() => setCodes(null)} />
      ) : setup ? (
        <SetupForm
          setup={setup}
          onCancel={() => setSetup(null)}
          onEnabled={(recoveryCodes) => {
            setSetup(null);
            setCodes(recoveryCodes);
          }}
        />
      ) : status.data.enabled ? (
        <Stack gap="md">
          <Text fz="sm">
            Signing in asks for a code from your authenticator app after the password.{" "}
            {status.data.recoveryCodesLeft === 1
              ? "1 recovery code is left."
              : `${status.data.recoveryCodesLeft} recovery codes are left.`}
          </Text>
          {status.data.recoveryCodesLeft <= 2 && (
            <Alert color="bronze" title="Few recovery codes left">
              Make new ones, so you can still sign in if you lose your phone.
            </Alert>
          )}
          <Group gap="sm">
            <Button variant="default" onClick={() => setAsking("codes")}>
              New recovery codes
            </Button>
            <Button variant="subtle" color="crimson" onClick={() => setAsking("disable")}>
              Turn off
            </Button>
          </Group>
        </Stack>
      ) : (
        <Stack gap="md">
          <Text fz="sm">
            Protect your account with a second step at sign-in: a 6-digit code from an authenticator app on
            your phone, such as Google Authenticator, Microsoft Authenticator, Aegis or 1Password. Someone who
            learns your password still cannot sign in.
          </Text>
          <Button onClick={begin} loading={start.isPending} w="fit-content">
            Set up two-factor sign-in
          </Button>
        </Stack>
      )}

      <PasswordPrompt
        opened={asking !== null}
        action={asking}
        onClose={() => setAsking(null)}
        onCodes={(recoveryCodes) => {
          setAsking(null);
          setCodes(recoveryCodes);
        }}
      />
    </Paper>
  );
}

function SetupForm({
  setup,
  onCancel,
  onEnabled,
}: {
  setup: TwoFactorSetup;
  onCancel: () => void;
  onEnabled: (codes: string[]) => void;
}) {
  const enable = useEnableTwoFactor();
  const [code, setCode] = useState("");

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    enable.mutate(code.trim(), {
      onSuccess: (result) => {
        notifications.show({ color: "healthy", message: "Two-factor sign-in is on." });
        onEnabled(result.recoveryCodes);
      },
    });
  };

  return (
    <form onSubmit={submit} noValidate>
      <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="lg">
        <Stack gap="sm">
          <Text fz="sm" fw={500}>
            1. Scan this with your authenticator app
          </Text>
          <Box p="sm" bg="white" w="fit-content" style={{ borderRadius: "var(--mantine-radius-sm)" }}>
            <QRCodeSVG
              value={setup.authenticatorUri}
              size={176}
              aria-label="QR code for your authenticator app"
            />
          </Box>
          <Text fz="xs" c="dimmed">
            Can&apos;t scan it? Add an account by hand, choose a time-based code, and type this key:
          </Text>
          <Group gap="xs">
            <Code fz="sm">{setup.sharedKey}</Code>
            <CopyButton value={setup.sharedKey.replace(/ /g, "")}>
              {({ copied, copy }) => (
                <Button variant="subtle" size="compact-xs" onClick={copy}>
                  {copied ? "Copied" : "Copy"}
                </Button>
              )}
            </CopyButton>
          </Group>
        </Stack>
        <Stack gap="sm">
          <Text fz="sm" fw={500}>
            2. Enter the code the app shows
          </Text>
          <TextInput
            aria-label="Code from the app"
            placeholder="123 456"
            autoComplete="one-time-code"
            inputMode="numeric"
            value={code}
            onChange={(event) => setCode(event.currentTarget.value)}
            error={enable.error ? errorText(enable.error, "code") : null}
            maw={200}
          />
          <Group gap="sm">
            <Button type="submit" loading={enable.isPending} disabled={code.trim() === ""}>
              Turn on
            </Button>
            <Button variant="default" onClick={onCancel}>
              Cancel
            </Button>
          </Group>
          <Text fz="xs" c="dimmed">
            Turning it on signs you out on every other device.
          </Text>
        </Stack>
      </SimpleGrid>
    </form>
  );
}

/** Recovery codes are shown once, straight after they are made. */
function RecoveryCodes({ codes, onDone }: { codes: string[]; onDone: () => void }) {
  const text = codes.join("\n");
  const download = () => {
    const url = URL.createObjectURL(new Blob([`Argus recovery codes\n\n${text}\n`], { type: "text/plain" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = "argus-recovery-codes.txt";
    link.click();
    URL.revokeObjectURL(url);
  };

  return (
    <Stack gap="md">
      <Alert color="bronze" title="Save these recovery codes now">
        If you lose your phone, each code signs you in once in place of the app. They are not shown again.
      </Alert>
      <SimpleGrid cols={{ base: 2, sm: 5 }} spacing="xs" maw={640}>
        {codes.map((code) => (
          <Code key={code} fz="sm" ta="center">
            {code}
          </Code>
        ))}
      </SimpleGrid>
      <Group gap="sm">
        <CopyButton value={text}>
          {({ copied, copy }) => (
            <Button variant="default" onClick={copy}>
              {copied ? "Copied" : "Copy codes"}
            </Button>
          )}
        </CopyButton>
        <Button variant="default" onClick={download}>
          Download
        </Button>
        <Button onClick={onDone}>I saved them</Button>
      </Group>
    </Stack>
  );
}

/** Turning two-factor off, or making new codes, asks for the password again. */
function PasswordPrompt({
  opened,
  action,
  onClose,
  onCodes,
}: {
  opened: boolean;
  action: "disable" | "codes" | null;
  onClose: () => void;
  onCodes: (codes: string[]) => void;
}) {
  const disable = useDisableTwoFactor();
  const regenerate = useRegenerateRecoveryCodes();
  const [password, setPassword] = useState("");
  const pending = disable.isPending || regenerate.isPending;
  const error = action === "disable" ? disable.error : regenerate.error;

  const close = () => {
    setPassword("");
    disable.reset();
    regenerate.reset();
    onClose();
  };

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    if (action === "disable") {
      disable.mutate(password, {
        onSuccess: () => {
          notifications.show({ color: "healthy", message: "Two-factor sign-in is off." });
          close();
        },
      });
    } else {
      regenerate.mutate(password, {
        onSuccess: (result) => {
          setPassword("");
          onCodes(result.recoveryCodes);
        },
      });
    }
  };

  return (
    <Modal
      opened={opened}
      onClose={close}
      title={action === "disable" ? "Turn off two-factor sign-in?" : "Make new recovery codes?"}
    >
      <form onSubmit={submit} noValidate>
        <Stack gap="md">
          <Text fz="sm">
            {action === "disable"
              ? "Signing in will only need your password, and your authenticator app's entry for Argus stops working."
              : "Your current recovery codes stop working, and new ones are shown once."}
          </Text>
          <PasswordInput
            label="Your password"
            autoComplete="current-password"
            data-autofocus
            value={password}
            onChange={(event) => setPassword(event.currentTarget.value)}
            error={error ? errorText(error, "password") : null}
          />
          <Group justify="flex-end" gap="sm">
            <Button variant="default" onClick={close}>
              Cancel
            </Button>
            <Button
              type="submit"
              color={action === "disable" ? "crimson" : undefined}
              loading={pending}
              disabled={password === ""}
            >
              {action === "disable" ? "Turn off" : "Make new codes"}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}

function errorText(error: ApiError, field: string): string {
  return error.fieldErrors[field] ?? error.message;
}
