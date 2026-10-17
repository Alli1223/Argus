import { Alert, Anchor, Button, Code, Group, List, PasswordInput, Stack, Text } from "@mantine/core";
import { modals } from "@mantine/modals";
import { notifications } from "@mantine/notifications";
import { useState } from "react";
import { Link } from "react-router";
import { useRemoveTelegramBot, useSaveTelegramSettings, useTelegramSettings } from "../../api/settings";
import type { TelegramSettings } from "../../api/types";
import { Section } from "../../components/Section";
import { formatAgo } from "../../lib/format";
import { useNow } from "../../lib/useNow";

/**
 * The one Telegram bot the server sends through. People then add their own chats as notification
 * channels; the bot tells each chat its ID.
 */
export function TelegramSettingsSection() {
  const settings = useTelegramSettings();

  return (
    <Section title="Telegram" mt="lg">
      <Stack p="md" gap="md">
        {settings.data ? (
          <TelegramForm settings={settings.data} />
        ) : (
          <Text fz="sm" c="dimmed">
            {settings.isError ? `Telegram settings did not load: ${settings.error.message}` : "Loading…"}
          </Text>
        )}
      </Stack>
    </Section>
  );
}

function TelegramForm({ settings }: { settings: TelegramSettings }) {
  const save = useSaveTelegramSettings();
  const remove = useRemoveTelegramBot();
  const [token, setToken] = useState("");

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    save.mutate(token.trim(), {
      onSuccess: (saved) => {
        setToken("");
        notifications.show({ color: "healthy", message: `Connected to @${saved.botUsername}.` });
      },
    });
  };

  const confirmRemove = () =>
    modals.openConfirmModal({
      title: "Remove the Telegram bot?",
      children: (
        <Text fz="sm">
          Every Telegram channel stops sending, and the buttons on alerts already sent stop working. The
          channels are kept, so adding a bot again brings them back.
        </Text>
      ),
      labels: { confirm: "Remove bot", cancel: "Keep it" },
      confirmProps: { color: "crimson" },
      onConfirm: () =>
        remove.mutate(undefined, {
          onSuccess: () => notifications.show({ color: "healthy", message: "Telegram bot removed." }),
        }),
    });

  return (
    <form onSubmit={submit} noValidate>
      <Stack gap="md">
        <Status settings={settings} />
        <SetupSteps />

        <Group gap="sm" align="flex-end" wrap="wrap">
          <PasswordInput
            label="Bot token"
            description={
              settings.configured
                ? "A token is saved. Paste a new one to switch to another bot."
                : "From BotFather. Argus checks it with Telegram before saving it."
            }
            placeholder={settings.configured ? "Unchanged" : "123456789:AAH4…"}
            value={token}
            onChange={(event) => setToken(event.currentTarget.value)}
            error={
              save.error?.fieldErrors.botToken ??
              (save.error && !save.error.fieldErrors.botToken ? save.error.message : null)
            }
            style={{ flex: "1 1 320px" }}
          />
          <Button type="submit" loading={save.isPending} disabled={token.trim() === ""}>
            {settings.configured ? "Replace bot" : "Connect bot"}
          </Button>
          {settings.configured && (
            <Button variant="subtle" color="gray" onClick={confirmRemove} loading={remove.isPending}>
              Remove bot
            </Button>
          )}
        </Group>
      </Stack>
    </form>
  );
}

function SetupSteps() {
  return (
    <List size="sm" spacing={4} type="ordered" c="dimmed">
      <List.Item>
        In Telegram, open{" "}
        <Anchor href="https://t.me/BotFather" target="_blank" rel="noreferrer" fz="sm">
          @BotFather
        </Anchor>{" "}
        and send <Code>/newbot</Code>.
      </List.Item>
      <List.Item>
        Give the bot a name, such as &ldquo;Argus alerts&rdquo;, and a username ending in <Code>bot</Code>.
      </List.Item>
      <List.Item>BotFather replies with a token. Paste it below and connect the bot.</List.Item>
      <List.Item>
        Each person then adds a Telegram channel under{" "}
        <Anchor component={Link} to="/notifications" fz="sm">
          Notifications
        </Anchor>
        , after sending the bot <Code>/start</Code> to get their chat ID.
      </List.Item>
    </List>
  );
}

function Status({ settings }: { settings: TelegramSettings }) {
  const now = useNow();
  if (!settings.configured) {
    return (
      <Alert color="bronze" title="No Telegram bot yet">
        Connect a bot and people can get their alerts in Telegram, and acknowledge or ignore them from there.
      </Alert>
    );
  }

  return (
    <Stack gap="xs">
      <Text fz="sm">
        Sending as{" "}
        <Anchor href={`https://t.me/${settings.botUsername}`} target="_blank" rel="noreferrer" fz="sm">
          @{settings.botUsername}
        </Anchor>
        {settings.botName ? ` (${settings.botName})` : ""}
        {settings.updatedBy ? `, connected by ${settings.updatedBy}` : ""}.
      </Text>
      {settings.lastError ? (
        <Alert color="crimson" title="Argus is not hearing from Telegram">
          {settings.lastError} Alerts still send, but the buttons on them and /start do nothing until this
          clears.
        </Alert>
      ) : (
        <Text fz="sm" c="dimmed">
          {settings.lastPolledAt
            ? `Listening for button presses; last heard from Telegram ${formatAgo(settings.lastPolledAt, now)}.`
            : "Starting to listen for button presses…"}
        </Text>
      )}
    </Stack>
  );
}
