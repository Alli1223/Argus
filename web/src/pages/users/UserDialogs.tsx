import {
  Alert,
  Button,
  Group,
  Modal,
  PasswordInput,
  SegmentedControl,
  Stack,
  Text,
  TextInput,
} from "@mantine/core";
import { hasLength, isEmail, useForm } from "@mantine/form";
import { notifications } from "@mantine/notifications";
import { useId } from "react";
import type { ApiError } from "../../api/client";
import type { UserSummary } from "../../api/types";
import {
  useCreateUser,
  useResetPassword,
  useUpdateUser,
  type NewUser,
  type Role,
  type UserChanges,
} from "../../api/users";
import { PASSWORD_MIN_LENGTH } from "../auth/accountValidation";

const ROLE_CHOICES: { label: string; value: Role }[] = [
  { label: "User", value: "User" },
  { label: "Administrator", value: "Admin" },
];

const passwordRule = hasLength(
  { min: PASSWORD_MIN_LENGTH },
  `Use at least ${PASSWORD_MIN_LENGTH} characters.`,
);

function nameError(value: string): string | null {
  if (value.trim() === "") return "Enter a name.";
  return value.trim().length > 100 ? "Names can be up to 100 characters." : null;
}

/** A problem the server reported that belongs to no single field, such as "Last administrator". */
function ServerError({ error }: { error: ApiError | null }) {
  if (!error || Object.keys(error.fieldErrors).length > 0) return null;
  return (
    <Alert color="crimson" title={error.title}>
      {error.message}
    </Alert>
  );
}

function RoleField({ value, onChange }: { value: Role; onChange: (role: Role) => void }) {
  const id = useId();
  return (
    <Stack gap={4}>
      <Text id={id} fz="sm" fw={500}>
        Role
      </Text>
      <SegmentedControl
        aria-labelledby={id}
        w="fit-content"
        data={ROLE_CHOICES}
        value={value}
        onChange={(role) => onChange(role as Role)}
      />
      <Text fz="xs" c="dimmed">
        Administrators see every system and manage who can sign in.
      </Text>
    </Stack>
  );
}

function Actions({ label, loading, onCancel }: { label: string; loading: boolean; onCancel: () => void }) {
  return (
    <Group justify="flex-end" gap="sm">
      <Button variant="default" onClick={onCancel}>
        Cancel
      </Button>
      <Button type="submit" loading={loading}>
        {label}
      </Button>
    </Group>
  );
}

interface DialogProps {
  opened: boolean;
  onClose: () => void;
}

export function AddUserDialog({ opened, onClose }: DialogProps) {
  return (
    <Modal opened={opened} onClose={onClose} title="Add a person">
      <AddUserForm onClose={onClose} />
    </Modal>
  );
}

function AddUserForm({ onClose }: { onClose: () => void }) {
  const create = useCreateUser();
  const form = useForm<NewUser>({
    initialValues: { displayName: "", email: "", password: "", role: "User" },
    validate: {
      displayName: nameError,
      email: isEmail("Enter a valid email address."),
      password: passwordRule,
    },
  });

  const submit = form.onSubmit((values) =>
    create.mutate(
      { ...values, displayName: values.displayName.trim(), email: values.email.trim() },
      {
        onSuccess: (user) => {
          notifications.show({
            color: "healthy",
            message: `${user.displayName} added. Give them their password in person or over a private channel.`,
          });
          onClose();
        },
        onError: (error) => form.setErrors(error.fieldErrors),
      },
    ),
  );

  return (
    <form onSubmit={submit} noValidate>
      <Stack gap="md">
        <TextInput label="Name" data-autofocus {...form.getInputProps("displayName")} />
        <TextInput
          label="Email"
          type="email"
          description="They sign in with this address."
          {...form.getInputProps("email")}
        />
        <PasswordInput
          label="Password"
          description={`At least ${PASSWORD_MIN_LENGTH} characters. They can change it on their account page.`}
          autoComplete="new-password"
          {...form.getInputProps("password")}
        />
        <RoleField value={form.values.role} onChange={(role) => form.setFieldValue("role", role)} />
        <ServerError error={create.error} />
        <Actions label="Add person" loading={create.isPending} onCancel={onClose} />
      </Stack>
    </form>
  );
}

interface UserDialogProps extends DialogProps {
  /** Kept while the dialog animates closed, so its content does not vanish mid-exit. */
  user: UserSummary | null;
}

export function EditUserDialog({ user, opened, onClose }: UserDialogProps) {
  return (
    <Modal opened={opened} onClose={onClose} title={user ? `Edit ${user.displayName}` : "Edit"}>
      {user && <EditUserForm user={user} onClose={onClose} />}
    </Modal>
  );
}

function EditUserForm({ user, onClose }: { user: UserSummary; onClose: () => void }) {
  const update = useUpdateUser();
  const form = useForm<UserChanges>({
    initialValues: { displayName: user.displayName, role: user.role },
    validate: { displayName: nameError },
  });

  const submit = form.onSubmit((values) =>
    update.mutate(
      { id: user.id, changes: { ...values, displayName: values.displayName.trim() } },
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
        <TextInput label="Name" data-autofocus {...form.getInputProps("displayName")} />
        <TextInput
          label="Email"
          description="Email addresses cannot be changed."
          value={user.email}
          readOnly
        />
        <RoleField value={form.values.role} onChange={(role) => form.setFieldValue("role", role)} />
        <ServerError error={update.error} />
        <Actions label="Save changes" loading={update.isPending} onCancel={onClose} />
      </Stack>
    </form>
  );
}

export function ResetPasswordDialog({ user, opened, onClose }: UserDialogProps) {
  return (
    <Modal
      opened={opened}
      onClose={onClose}
      title={user ? `New password for ${user.displayName}` : "New password"}
    >
      {user && <ResetPasswordForm user={user} onClose={onClose} />}
    </Modal>
  );
}

function ResetPasswordForm({ user, onClose }: { user: UserSummary; onClose: () => void }) {
  const reset = useResetPassword();
  const form = useForm({ initialValues: { newPassword: "" }, validate: { newPassword: passwordRule } });

  const submit = form.onSubmit(({ newPassword }) =>
    reset.mutate(
      { id: user.id, newPassword },
      {
        onSuccess: () => {
          notifications.show({ color: "healthy", message: `Password changed for ${user.displayName}.` });
          onClose();
        },
        onError: (error) => form.setErrors(error.fieldErrors),
      },
    ),
  );

  return (
    <form onSubmit={submit} noValidate>
      <Stack gap="md">
        <Text fz="sm">
          Their other sessions end, and an account locked after failed sign-ins is unlocked. Give them the new
          password privately.
        </Text>
        <PasswordInput
          label="New password"
          description={`At least ${PASSWORD_MIN_LENGTH} characters.`}
          autoComplete="new-password"
          data-autofocus
          {...form.getInputProps("newPassword")}
        />
        <ServerError error={reset.error} />
        <Actions label="Set password" loading={reset.isPending} onCancel={onClose} />
      </Stack>
    </form>
  );
}
