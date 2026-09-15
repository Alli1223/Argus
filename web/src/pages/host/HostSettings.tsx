import {
  Alert,
  Button,
  Divider,
  Group,
  Modal,
  Stack,
  TagsInput,
  Text,
  TextInput,
  Textarea,
} from "@mantine/core";
import { useForm } from "@mantine/form";
import { notifications } from "@mantine/notifications";
import { useUpdateHost } from "../../api/hosts";
import type { HostDetail } from "../../api/types";
import { MAX_TAGS, normalizeTags, tagsError } from "../../lib/tags";

// The server's limits, checked here first so mistakes show beside the field.
const MAX_NAME = 256;
const MAX_NOTES = 4000;

interface SettingsValues {
  displayName: string;
  tags: string[];
  notes: string;
}

interface HostSettingsProps {
  host: HostDetail;
  opened: boolean;
  onClose: () => void;
  /** Starts deleting the host; the settings close first. */
  onDelete: () => void;
}

/** Rename a host, change its tags and notes, or delete it. */
export function HostSettings({ host, opened, onClose, onDelete }: HostSettingsProps) {
  return (
    <Modal opened={opened} onClose={onClose} title="Host settings" size="lg">
      <SettingsForm host={host} onClose={onClose} onDelete={onDelete} />
    </Modal>
  );
}

// Mounted each time the dialog opens, so it always starts from the host as it is now.
function SettingsForm({ host, onClose, onDelete }: Omit<HostSettingsProps, "opened">) {
  const update = useUpdateHost(host.id);
  const form = useForm<SettingsValues>({
    initialValues: { displayName: host.displayName, tags: host.tags, notes: host.notes ?? "" },
    validate: {
      displayName: (value) => {
        if (value.trim() === "") return "Give the host a name.";
        return value.trim().length > MAX_NAME ? `Names can be up to ${MAX_NAME} characters.` : null;
      },
      tags: tagsError,
      notes: (value) => (value.length > MAX_NOTES ? "Notes can be up to 4,000 characters." : null),
    },
  });

  const submit = form.onSubmit((values) =>
    update.mutate(
      { displayName: values.displayName.trim(), tags: values.tags, notes: values.notes },
      {
        onSuccess: () => {
          notifications.show({ color: "healthy", message: "Changes saved." });
          onClose();
        },
        onError: (error) => form.setErrors(error.fieldErrors),
      },
    ),
  );

  return (
    <form onSubmit={submit} noValidate>
      <Stack gap="md">
        <TextInput
          label="Name"
          description="Shown everywhere in Argus. The machine keeps its own hostname."
          required
          data-autofocus
          {...form.getInputProps("displayName")}
        />
        <TagsInput
          label="Tags"
          description="For filtering hosts and scoping alert rules. Press Enter or space after each one."
          placeholder="Add a tag"
          splitChars={[",", " "]}
          maxTags={MAX_TAGS}
          clearable
          {...form.getInputProps("tags")}
          onChange={(tags) => form.setFieldValue("tags", normalizeTags(tags))}
        />
        <Textarea
          label="Notes"
          description="Anything worth knowing about this machine, like where it is or who looks after it."
          autosize
          minRows={3}
          maxRows={10}
          {...form.getInputProps("notes")}
        />
        {update.error && Object.keys(update.error.fieldErrors).length === 0 && (
          <Alert color="crimson" title={update.error.title}>
            {update.error.message}
          </Alert>
        )}
        <Group justify="flex-end" gap="sm">
          <Button variant="default" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={update.isPending}>
            Save changes
          </Button>
        </Group>

        <Divider />
        <Group justify="space-between" align="center" wrap="wrap" gap="sm">
          <div>
            <Text fz="sm" fw={600}>
              Delete this host
            </Text>
            <Text fz="xs" c="dimmed">
              Removes it with all of its history and alerts.
            </Text>
          </div>
          <Button
            color="crimson"
            variant="light"
            onClick={() => {
              onClose();
              onDelete();
            }}
          >
            Delete host
          </Button>
        </Group>
      </Stack>
    </form>
  );
}
