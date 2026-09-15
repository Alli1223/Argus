import { Anchor, Box, Button, Group, Skeleton, Switch, Table, Text, VisuallyHidden } from "@mantine/core";
import { modals } from "@mantine/modals";
import { notifications } from "@mantine/notifications";
import { IconPlus } from "@tabler/icons-react";
import { useState } from "react";
import { Link } from "react-router";
import { useAlertRules, useDeleteAlertRule, useSaveAlertRule } from "../../api/alerts";
import type { AlertRule } from "../../api/types";
import { PageHeader } from "../../components/PageHeader";
import { ErrorScreen } from "../../components/Screens";
import { SeverityLabel } from "../../components/SeverityLabel";
import { RuleEditor } from "./RuleEditor";
import { describeRule, describeScope, ruleToRequest } from "./ruleText";

export function RulesPage() {
  const rules = useAlertRules();
  // The rule stays set while the dialog animates closed, so its title does not flip mid-exit.
  const [editor, setEditor] = useState<{ opened: boolean; rule: AlertRule | null }>({
    opened: false,
    rule: null,
  });
  const open = (rule: AlertRule | null) => setEditor({ opened: true, rule });

  if (rules.isError) return <ErrorScreen error={rules.error} onRetry={() => void rules.refetch()} />;

  return (
    <>
      <PageHeader
        title="Alert rules"
        description="When Argus raises an alert. A rule fires once its condition has held for its whole duration on a host it applies to."
        actions={
          <Button leftSection={<IconPlus size={16} />} onClick={() => open(null)}>
            New rule
          </Button>
        }
      />

      <Box className="argus-surface" style={{ borderRadius: "var(--mantine-radius-sm)", overflowX: "auto" }}>
        <Table miw={860}>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>On</Table.Th>
              <Table.Th>Rule</Table.Th>
              <Table.Th>Applies to</Table.Th>
              <Table.Th>Severity</Table.Th>
              <Table.Th>Firing</Table.Th>
              <Table.Th>
                <VisuallyHidden>Actions</VisuallyHidden>
              </Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {rules.isPending &&
              Array.from({ length: 4 }, (_, index) => (
                <Table.Tr key={index}>
                  <Table.Td colSpan={6}>
                    <Skeleton h={28} />
                  </Table.Td>
                </Table.Tr>
              ))}
            {rules.data?.map((rule) => (
              <RuleRow key={rule.id} rule={rule} onEdit={() => open(rule)} />
            ))}
            {rules.data?.length === 0 && (
              <Table.Tr>
                <Table.Td colSpan={6}>
                  <Group gap="sm" py="md" justify="center">
                    <Text fz="sm" c="dimmed">
                      No rules, so nothing raises an alert.
                    </Text>
                    <Button variant="subtle" size="compact-sm" onClick={() => open(null)}>
                      New rule
                    </Button>
                  </Group>
                </Table.Td>
              </Table.Tr>
            )}
          </Table.Tbody>
        </Table>
      </Box>

      <RuleEditor
        rule={editor.rule}
        opened={editor.opened}
        onClose={() => setEditor((current) => ({ ...current, opened: false }))}
      />
    </>
  );
}

function RuleRow({ rule, onEdit }: { rule: AlertRule; onEdit: () => void }) {
  const save = useSaveAlertRule();
  const remove = useDeleteAlertRule();

  const toggle = (enabled: boolean) =>
    save.mutate(
      { id: rule.id, rule: { ...ruleToRequest(rule), enabled } },
      {
        onError: (error) =>
          notifications.show({ color: "crimson", title: "The rule was not changed", message: error.message }),
      },
    );

  const confirmDelete = () =>
    modals.openConfirmModal({
      title: `Delete ${rule.name}?`,
      children: <Text fz="sm">Its firing alerts are resolved. Past alerts stay in the history.</Text>,
      labels: { confirm: "Delete rule", cancel: "Keep it" },
      confirmProps: { color: "crimson" },
      onConfirm: () =>
        remove.mutate(rule.id, {
          onSuccess: () => notifications.show({ color: "healthy", message: `${rule.name} deleted.` }),
          onError: (error) =>
            notifications.show({
              color: "crimson",
              title: "The rule was not deleted",
              message: error.message,
            }),
        }),
    });

  return (
    <Table.Tr>
      <Table.Td>
        <Switch
          checked={rule.enabled}
          disabled={save.isPending}
          onChange={(event) => toggle(event.currentTarget.checked)}
          aria-label={rule.name}
        />
      </Table.Td>
      <Table.Td>
        <Text fz="sm" fw={600} c={rule.enabled ? undefined : "dimmed"}>
          {rule.name}
        </Text>
        <Text fz="xs" c="dimmed">
          {describeRule(rule)}
        </Text>
      </Table.Td>
      <Table.Td>{describeScope(rule)}</Table.Td>
      <Table.Td>
        <SeverityLabel severity={rule.severity} />
      </Table.Td>
      <Table.Td>
        {rule.firingAlerts > 0 ? (
          <Anchor component={Link} to="/alerts" fz="sm">
            {rule.firingAlerts} firing
          </Anchor>
        ) : (
          <Text fz="sm" c="dimmed">
            None
          </Text>
        )}
      </Table.Td>
      <Table.Td>
        <Group gap={4} justify="flex-end" wrap="nowrap">
          <Button variant="subtle" size="compact-sm" onClick={onEdit} aria-label={`Edit ${rule.name}`}>
            Edit
          </Button>
          <Button
            variant="subtle"
            color="crimson"
            size="compact-sm"
            onClick={confirmDelete}
            aria-label={`Delete ${rule.name}`}
          >
            Delete
          </Button>
        </Group>
      </Table.Td>
    </Table.Tr>
  );
}
