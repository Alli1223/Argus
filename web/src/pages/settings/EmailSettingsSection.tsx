import {
  Alert,
  Anchor,
  Button,
  Group,
  NumberInput,
  PasswordInput,
  Select,
  Stack,
  Text,
  TextInput,
} from "@mantine/core";
import { useForm } from "@mantine/form";
import { modals } from "@mantine/modals";
import { notifications } from "@mantine/notifications";
import { IconMail } from "@tabler/icons-react";
import { useState } from "react";
import { Link } from "react-router";
import { useCurrentUser } from "../../api/auth";
import {
  useEmailSettings,
  useForgetEmailSettings,
  useSaveEmailSettings,
  useSendTestEmail,
} from "../../api/settings";
import type { EmailSettings, EmailSettingsRequest, SmtpSecurity } from "../../api/types";
import { Section } from "../../components/Section";

const SECURITY_CHOICES: { value: SmtpSecurity; label: string }[] = [
  { value: "Auto", label: "Automatic" },
  { value: "StartTls", label: "STARTTLS" },
  { value: "SslOnConnect", label: "TLS on connect" },
  { value: "None", label: "None" },
];

/** What a form starts from, and what an empty form looks like. */
const BLANK: EmailSettingsRequest = {
  host: "",
  port: 587,
  security: "Auto",
  username: "",
  password: "",
  from: "",
  fromName: "Argus",
};

/**
 * The mail server alert emails go through. Settings saved here are used in place of anything the
 * Compose file sets, so email can be set up without touching the machine Argus runs on.
 */
export function EmailSettingsSection() {
  const settings = useEmailSettings();

  return (
    <Section title="Email" mt="lg">
      <Stack p="md" gap="md">
        {settings.data ? (
          <EmailSettingsForm settings={settings.data} />
        ) : (
          <Text fz="sm" c="dimmed">
            {settings.isError ? `Email settings did not load: ${settings.error.message}` : "Loading…"}
          </Text>
        )}
      </Stack>
    </Section>
  );
}

function EmailSettingsForm({ settings }: { settings: EmailSettings }) {
  const save = useSaveEmailSettings();
  const forget = useForgetEmailSettings();
  const test = useSendTestEmail();
  const me = useCurrentUser();
  const [testTo, setTestTo] = useState("");

  const form = useForm<EmailSettingsRequest>({
    initialValues: {
      ...BLANK,
      host: settings.host ?? "",
      port: settings.port,
      security: settings.security,
      username: settings.username ?? "",
      password: "",
      from: settings.from ?? "",
      fromName: settings.fromName,
    },
    validate: {
      host: (value) => (value.trim() === "" ? "Enter the mail server, such as smtp.gmail.com." : null),
      from: (value) => (value.trim() === "" ? "Enter the address emails come from." : null),
    },
  });

  const submit = form.onSubmit((values) =>
    save.mutate(
      {
        host: values.host.trim(),
        port: values.port,
        security: values.security,
        username: values.username?.trim() || null,
        // An empty box keeps the saved password; clearing it is its own button.
        password: values.password ? values.password : undefined,
        from: values.from.trim(),
        fromName: values.fromName.trim() || "Argus",
      },
      {
        onSuccess: () => {
          form.setFieldValue("password", "");
          form.resetDirty();
          notifications.show({ color: "healthy", message: "Email settings saved." });
        },
        onError: (error) => form.setErrors(error.fieldErrors),
      },
    ),
  );

  const sendTest = () => {
    const to = testTo.trim() || me.data?.email || "";
    test.mutate(to, {
      onSuccess: () => notifications.show({ color: "healthy", message: `Test email sent to ${to}.` }),
      onError: (error) =>
        notifications.show({
          color: "critical",
          title: "The test was not sent",
          message: error.message,
          autoClose: false,
        }),
    });
  };

  const confirmForget = () =>
    modals.openConfirmModal({
      title: "Forget these settings?",
      children: (
        <Text fz="sm">
          Argus goes back to the mail server in the Compose file, if there is one. Without one, alert emails
          stop.
        </Text>
      ),
      labels: { confirm: "Forget settings", cancel: "Cancel" },
      confirmProps: { color: "critical" },
      onConfirm: () =>
        forget.mutate(undefined, {
          onSuccess: () => notifications.show({ color: "healthy", message: "Saved settings forgotten." }),
        }),
    });

  return (
    <form onSubmit={submit} noValidate>
      <Stack gap="md">
        <Status settings={settings} />

        <Group align="flex-start" gap="md" grow wrap="wrap">
          <TextInput
            label="Mail server"
            description="For a Gmail account, smtp.gmail.com."
            placeholder="smtp.gmail.com"
            required
            {...form.getInputProps("host")}
          />
          <NumberInput
            label="Port"
            description="587 for STARTTLS, 465 for TLS."
            min={1}
            max={65535}
            clampBehavior="strict"
            allowDecimal={false}
            {...form.getInputProps("port")}
          />
          <Select
            label="Security"
            description="Automatic suits almost every mail server."
            data={SECURITY_CHOICES}
            allowDeselect={false}
            {...form.getInputProps("security")}
          />
        </Group>

        <Group align="flex-start" gap="md" grow wrap="wrap">
          <TextInput
            label="Username"
            description="Usually the whole email address. Leave empty if the server needs no sign-in."
            placeholder="you@gmail.com"
            {...form.getInputProps("username")}
          />
          <PasswordInput
            label="Password"
            description={
              settings.hasPassword
                ? "A password is saved. Type a new one to replace it."
                : "For Gmail, an app password from your Google account."
            }
            placeholder={settings.hasPassword ? "Unchanged" : ""}
            {...form.getInputProps("password")}
          />
        </Group>

        <Group align="flex-start" gap="md" grow wrap="wrap">
          <TextInput
            label="From address"
            description="Gmail only sends as your own address."
            placeholder="you@gmail.com"
            required
            {...form.getInputProps("from")}
          />
          <TextInput label="From name" placeholder="Argus" {...form.getInputProps("fromName")} />
        </Group>

        <Group gap="sm">
          <Button type="submit" loading={save.isPending}>
            Save
          </Button>
          {settings.source === "App" && (
            <Button variant="subtle" color="gray" onClick={confirmForget} loading={forget.isPending}>
              Forget these settings
            </Button>
          )}
        </Group>

        <Stack gap="xs">
          <Text fz="sm" fw={500}>
            Send a test email
          </Text>
          <Text fz="sm" c="dimmed">
            The test goes out with the settings as saved, so save any changes first.
          </Text>
          <Group gap="sm" align="flex-end" wrap="wrap">
            <TextInput
              aria-label="Send the test to"
              placeholder={me.data?.email ?? "you@example.com"}
              value={testTo}
              onChange={(event) => setTestTo(event.currentTarget.value)}
              style={{ flex: "1 1 260px" }}
            />
            <Button
              variant="default"
              leftSection={<IconMail size={16} />}
              onClick={sendTest}
              loading={test.isPending}
              disabled={!settings.configured}
            >
              Send test
            </Button>
          </Group>
        </Stack>
      </Stack>
    </form>
  );
}

const savedFormat = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" });

function Status({ settings }: { settings: EmailSettings }) {
  if (!settings.configured) {
    return (
      <Alert color="bronze" title="Argus cannot send email yet">
        Fill this in and alerts can be emailed. People then choose where their own alerts go under{" "}
        <Anchor component={Link} to="/notifications">
          Notifications
        </Anchor>
        .
      </Alert>
    );
  }

  if (settings.source === "File") {
    return (
      <Alert color="blue" title="These settings come from the Compose file">
        Argus sends through {settings.host} as {settings.from}. Saving here replaces those settings, and the
        Compose file is left alone.
      </Alert>
    );
  }

  const saved = settings.updatedAt ? savedFormat.format(new Date(settings.updatedAt)) : null;
  return (
    <Text fz="sm" c="dimmed">
      Argus sends through {settings.host} as {settings.from}
      {saved ? `, saved ${saved}` : ""}
      {settings.updatedBy ? ` by ${settings.updatedBy}` : ""}.
    </Text>
  );
}
