import {
  Alert,
  Button,
  Paper,
  PasswordInput,
  SimpleGrid,
  Skeleton,
  Stack,
  Text,
  TextInput,
  Title,
} from "@mantine/core";
import { hasLength, isNotEmpty, useForm } from "@mantine/form";
import { notifications } from "@mantine/notifications";
import type { ReactNode } from "react";
import { useChangePassword, useUpdateProfile } from "../../api/account";
import { useCurrentUser } from "../../api/auth";
import type { CurrentUser } from "../../api/types";
import { PageHeader } from "../../components/PageHeader";
import { PASSWORD_MIN_LENGTH } from "../auth/accountValidation";

export function AccountPage() {
  const me = useCurrentUser();

  return (
    <>
      <PageHeader title="Account" description="Your name, and the password you sign in with." />
      <SimpleGrid cols={{ base: 1, md: 2 }} spacing="lg" maw={960}>
        {me.data ? <ProfileForm user={me.data} /> : <Skeleton h={240} radius="sm" />}
        <PasswordForm />
      </SimpleGrid>
    </>
  );
}

function Panel({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Paper className="argus-surface" p="lg" radius="sm">
      <Title order={2} fz={17} mb="md">
        {title}
      </Title>
      {children}
    </Paper>
  );
}

function ProfileForm({ user }: { user: CurrentUser }) {
  const update = useUpdateProfile();
  const form = useForm({
    initialValues: { displayName: user.displayName },
    validate: {
      displayName: (value: string) => {
        if (value.trim() === "") return "Enter a name.";
        return value.trim().length > 100 ? "Names can be up to 100 characters." : null;
      },
    },
  });

  const submit = form.onSubmit((values) =>
    update.mutate(
      { displayName: values.displayName.trim() },
      {
        onSuccess: (saved) => {
          form.resetDirty({ displayName: saved.displayName });
          notifications.show({ color: "healthy", message: "Name saved." });
        },
        onError: (error) => form.setErrors(error.fieldErrors),
      },
    ),
  );

  return (
    <Panel title="Profile">
      <form onSubmit={submit} noValidate>
        <Stack gap="md">
          <TextInput
            label="Name"
            description="Shown to other people on this server."
            autoComplete="name"
            {...form.getInputProps("displayName")}
          />
          <TextInput label="Email" description="You sign in with this address." value={user.email} readOnly />
          <Text fz="sm" c="dimmed">
            {user.isAdmin
              ? "You are an administrator: you see every system and manage who can sign in."
              : "You see the systems you added."}
          </Text>
          {update.error && Object.keys(update.error.fieldErrors).length === 0 && (
            <Alert color="crimson" title={update.error.title}>
              {update.error.message}
            </Alert>
          )}
          <Button type="submit" loading={update.isPending} disabled={!form.isDirty()} w="fit-content">
            Save name
          </Button>
        </Stack>
      </form>
    </Panel>
  );
}

interface PasswordValues {
  currentPassword: string;
  newPassword: string;
  confirmPassword: string;
}

function PasswordForm() {
  const change = useChangePassword();
  const form = useForm<PasswordValues>({
    initialValues: { currentPassword: "", newPassword: "", confirmPassword: "" },
    validate: {
      currentPassword: isNotEmpty("Enter your current password."),
      newPassword: hasLength({ min: PASSWORD_MIN_LENGTH }, `Use at least ${PASSWORD_MIN_LENGTH} characters.`),
      confirmPassword: (value, values) =>
        value !== values.newPassword ? "The passwords do not match." : null,
    },
  });

  const submit = form.onSubmit(({ currentPassword, newPassword }) =>
    change.mutate(
      { currentPassword, newPassword },
      {
        onSuccess: () => {
          form.reset();
          notifications.show({
            color: "healthy",
            title: "Password changed",
            message: "You stay signed in here. Everywhere else is signed out.",
          });
        },
        onError: (error) => form.setErrors(error.fieldErrors),
      },
    ),
  );

  return (
    <Panel title="Password">
      <form onSubmit={submit} noValidate>
        <Stack gap="md">
          <PasswordInput
            label="Current password"
            autoComplete="current-password"
            {...form.getInputProps("currentPassword")}
          />
          <PasswordInput
            label="New password"
            description={`At least ${PASSWORD_MIN_LENGTH} characters. A few unrelated words make a strong one.`}
            autoComplete="new-password"
            {...form.getInputProps("newPassword")}
          />
          <PasswordInput
            label="New password again"
            autoComplete="new-password"
            {...form.getInputProps("confirmPassword")}
          />
          {change.error && Object.keys(change.error.fieldErrors).length === 0 && (
            <Alert color="crimson" title={change.error.title}>
              {change.error.message}
            </Alert>
          )}
          <Text fz="xs" c="dimmed">
            Changing it signs you out on every other device.
          </Text>
          <Button type="submit" loading={change.isPending} w="fit-content">
            Change password
          </Button>
        </Stack>
      </form>
    </Panel>
  );
}
