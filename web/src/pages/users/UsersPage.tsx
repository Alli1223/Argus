import {
  ActionIcon,
  Avatar,
  Box,
  Button,
  Group,
  Menu,
  Skeleton,
  Table,
  Text,
  VisuallyHidden,
} from "@mantine/core";
import { modals } from "@mantine/modals";
import { notifications } from "@mantine/notifications";
import {
  IconDots,
  IconKey,
  IconPencil,
  IconPlus,
  IconTrash,
  IconUserCheck,
  IconUserOff,
} from "@tabler/icons-react";
import { useState } from "react";
import { useCurrentUser } from "../../api/auth";
import type { ApiError } from "../../api/client";
import type { UserSummary } from "../../api/types";
import { useDeleteUser, useSetUserDisabled, useUsers } from "../../api/users";
import { PageHeader } from "../../components/PageHeader";
import { ErrorScreen } from "../../components/Screens";
import { formatAgo } from "../../lib/format";
import { useNow } from "../../lib/useNow";
import { AddUserDialog, EditUserDialog, ResetPasswordDialog } from "./UserDialogs";
import { userStatus, type UserStatus } from "./userStatus";

const STATUS_COLORS: Record<UserStatus, string> = {
  Active: "healthy",
  Disabled: "gray",
  "Locked out": "bronze",
};

const day = new Intl.DateTimeFormat(undefined, { dateStyle: "medium" });

type DialogKind = "add" | "edit" | "reset";

export function UsersPage() {
  const users = useUsers();
  const me = useCurrentUser();
  const now = useNow();
  // The person stays set while a dialog animates closed.
  const [dialog, setDialog] = useState<{ kind: DialogKind; user: UserSummary | null; opened: boolean }>({
    kind: "add",
    user: null,
    opened: false,
  });
  const open = (kind: DialogKind, user: UserSummary | null = null) => setDialog({ kind, user, opened: true });
  const close = () => setDialog((current) => ({ ...current, opened: false }));

  if (users.isError) return <ErrorScreen error={users.error} onRetry={() => void users.refetch()} />;

  return (
    <>
      <PageHeader
        title="Users"
        description="People who can sign in to this server. Administrators see every system; everyone else sees the systems they added."
        actions={
          <Button leftSection={<IconPlus size={16} />} onClick={() => open("add")}>
            Add a person
          </Button>
        }
      />

      <Box className="argus-surface" style={{ borderRadius: "var(--mantine-radius-sm)", overflowX: "auto" }}>
        <Table miw={760}>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Person</Table.Th>
              <Table.Th>Role</Table.Th>
              <Table.Th>Status</Table.Th>
              <Table.Th>Last sign-in</Table.Th>
              <Table.Th>Added</Table.Th>
              <Table.Th>
                <VisuallyHidden>Actions</VisuallyHidden>
              </Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {users.isPending &&
              Array.from({ length: 3 }, (_, index) => (
                <Table.Tr key={index}>
                  <Table.Td colSpan={6}>
                    <Skeleton h={30} />
                  </Table.Td>
                </Table.Tr>
              ))}
            {users.data?.map((user) => (
              <UserRow
                key={user.id}
                user={user}
                isMe={user.id === me.data?.id}
                now={now}
                onEdit={() => open("edit", user)}
                onReset={() => open("reset", user)}
              />
            ))}
          </Table.Tbody>
        </Table>
      </Box>

      <AddUserDialog opened={dialog.opened && dialog.kind === "add"} onClose={close} />
      <EditUserDialog user={dialog.user} opened={dialog.opened && dialog.kind === "edit"} onClose={close} />
      <ResetPasswordDialog
        user={dialog.user}
        opened={dialog.opened && dialog.kind === "reset"}
        onClose={close}
      />
    </>
  );
}

interface UserRowProps {
  user: UserSummary;
  isMe: boolean;
  now: number;
  onEdit: () => void;
  onReset: () => void;
}

function UserRow({ user, isMe, now, onEdit, onReset }: UserRowProps) {
  const setDisabled = useSetUserDisabled();
  const remove = useDeleteUser();
  const status = userStatus(user);

  const failed = (title: string) => (error: ApiError) =>
    notifications.show({ color: "crimson", title, message: error.message });

  const enable = () =>
    setDisabled.mutate(
      { id: user.id, disabled: false },
      {
        onSuccess: () =>
          notifications.show({ color: "healthy", message: `${user.displayName} can sign in again.` }),
        onError: failed("The account was not enabled"),
      },
    );

  const confirmDisable = () =>
    modals.openConfirmModal({
      title: `Disable ${user.displayName}?`,
      children: (
        <Text fz="sm">
          They are signed out within a minute and cannot sign in until you enable the account again. Their
          systems, rules and history are kept.
        </Text>
      ),
      labels: { confirm: "Disable account", cancel: "Keep it" },
      confirmProps: { color: "crimson" },
      onConfirm: () =>
        setDisabled.mutate(
          { id: user.id, disabled: true },
          {
            onSuccess: () =>
              notifications.show({ color: "healthy", message: `${user.displayName} disabled.` }),
            onError: failed("The account was not disabled"),
          },
        ),
    });

  const confirmDelete = () =>
    modals.openConfirmModal({
      title: `Delete ${user.displayName}?`,
      children: (
        <Text fz="sm">
          This removes their account and everything that belongs to it: their systems with all of their
          history, their alert rules and their enrollment tokens. It cannot be undone.
        </Text>
      ),
      labels: { confirm: "Delete person", cancel: "Keep them" },
      confirmProps: { color: "crimson" },
      onConfirm: () =>
        remove.mutate(user.id, {
          onSuccess: () => notifications.show({ color: "healthy", message: `${user.displayName} deleted.` }),
          onError: failed("The person was not deleted"),
        }),
    });

  return (
    <Table.Tr>
      <Table.Td>
        <Group gap="sm" wrap="nowrap">
          <Avatar size={30} radius="xl" color="iris" name={user.displayName} />
          <div>
            <Text fz="sm" fw={600}>
              {user.displayName}
              {isMe && (
                <Text span fz="xs" c="dimmed" fw={400}>
                  {" "}
                  (you)
                </Text>
              )}
            </Text>
            <Text fz="xs" c="dimmed">
              {user.email}
            </Text>
          </div>
        </Group>
      </Table.Td>
      <Table.Td>{user.role === "Admin" ? "Administrator" : "User"}</Table.Td>
      <Table.Td>
        <Group gap={6} wrap="nowrap">
          <Box
            w={8}
            h={8}
            style={{ borderRadius: 4, background: `var(--mantine-color-${STATUS_COLORS[status]}-filled)` }}
            aria-hidden
          />
          {status}
        </Group>
      </Table.Td>
      <Table.Td>{user.lastLoginAt ? formatAgo(user.lastLoginAt, now) : "Never"}</Table.Td>
      <Table.Td>{day.format(new Date(user.createdAt))}</Table.Td>
      <Table.Td ta="right">
        <Menu position="bottom-end" width={200}>
          <Menu.Target>
            <ActionIcon variant="subtle" color="gray" aria-label={`Actions for ${user.displayName}`}>
              <IconDots size={18} />
            </ActionIcon>
          </Menu.Target>
          <Menu.Dropdown>
            <Menu.Item leftSection={<IconPencil size={16} />} onClick={onEdit}>
              Edit
            </Menu.Item>
            {/* Your own password is changed on the account page, which asks for the current one. */}
            {!isMe && (
              <Menu.Item leftSection={<IconKey size={16} />} onClick={onReset}>
                Set a new password
              </Menu.Item>
            )}
            {!isMe &&
              (user.isDisabled ? (
                <Menu.Item leftSection={<IconUserCheck size={16} />} onClick={enable}>
                  Enable account
                </Menu.Item>
              ) : (
                <Menu.Item leftSection={<IconUserOff size={16} />} onClick={confirmDisable}>
                  Disable account
                </Menu.Item>
              ))}
            {!isMe && (
              <>
                <Menu.Divider />
                <Menu.Item color="crimson" leftSection={<IconTrash size={16} />} onClick={confirmDelete}>
                  Delete
                </Menu.Item>
              </>
            )}
          </Menu.Dropdown>
        </Menu>
      </Table.Td>
    </Table.Tr>
  );
}
