import {
  AppShell,
  Avatar,
  Badge,
  Burger,
  Group,
  Menu,
  NavLink,
  Text,
  UnstyledButton,
  useMantineColorScheme,
  type MantineColorScheme,
} from "@mantine/core";
import { useDisclosure } from "@mantine/hooks";
import {
  IconArrowUpCircle,
  IconAdjustmentsHorizontal,
  IconBell,
  IconCircleCheck,
  IconDeviceDesktopAnalytics,
  IconEye,
  IconLogout,
  IconMoon,
  IconPlus,
  IconSend,
  IconServer2,
  IconSun,
  IconTemperature,
  IconUserCircle,
  IconUsers,
} from "@tabler/icons-react";
import type { ComponentType } from "react";
import { Link, Outlet, useLocation, useNavigate, useNavigation } from "react-router";
import { useAlertCounts } from "../api/alerts";
import { useCurrentUser, useLogout } from "../api/auth";
import { useLiveUpdates } from "../api/live";
import { useServerInfo } from "../api/server";
import { useServerUpdate } from "../api/updates";
import { LiveIndicator } from "../components/LiveIndicator";
import { ServerUpdateBadge, ServerUpdateModal } from "../components/ServerUpdate";
import { Wordmark } from "../components/Wordmark";
import classes from "./AppLayout.module.css";

interface NavItem {
  to: string;
  label: string;
  icon: ComponentType<{ size?: number; stroke?: number }>;
  adminOnly?: boolean;
}

const navItems: NavItem[] = [
  { to: "/", label: "Overview", icon: IconEye },
  { to: "/hosts", label: "Hosts", icon: IconServer2 },
  { to: "/temperatures", label: "Temperatures", icon: IconTemperature },
  { to: "/alerts", label: "Alerts", icon: IconBell },
  { to: "/rules", label: "Alert rules", icon: IconAdjustmentsHorizontal },
  { to: "/notifications", label: "Notifications", icon: IconSend },
  { to: "/systems", label: "Add a system", icon: IconPlus },
  { to: "/users", label: "Users", icon: IconUsers, adminOnly: true },
];

function isActive(pathname: string, to: string) {
  return to === "/" ? pathname === "/" : pathname === to || pathname.startsWith(`${to}/`);
}

export function AppLayout() {
  const [opened, { toggle, close }] = useDisclosure();
  const { pathname } = useLocation();
  const me = useCurrentUser();
  const live = useLiveUpdates();
  const navigation = useNavigation();

  return (
    <AppShell
      header={{ height: 52 }}
      navbar={{ width: 216, breakpoint: "sm", collapsed: { mobile: !opened } }}
      padding="lg"
    >
      <AppShell.Header>
        <Group h="100%" px="md" justify="space-between" wrap="nowrap">
          <Group gap="sm" wrap="nowrap">
            <Burger
              opened={opened}
              onClick={toggle}
              hiddenFrom="sm"
              size="sm"
              aria-label="Toggle navigation"
            />
            <Link to="/" aria-label="Argus overview" style={{ textDecoration: "none" }}>
              <Wordmark />
            </Link>
          </Group>
          <Group gap="sm" wrap="nowrap">
            <ServerUpdateBadge />
            <LiveIndicator state={live} />
            <AlertChips />
            <UserMenu />
          </Group>
        </Group>
      </AppShell.Header>

      <AppShell.Navbar p="xs">
        {navItems
          .filter((item) => !item.adminOnly || me.data?.isAdmin)
          .map((item) => (
            <NavLink
              key={item.to}
              component={Link}
              to={item.to}
              label={item.label}
              leftSection={<item.icon size={18} stroke={1.6} />}
              active={isActive(pathname, item.to)}
              onClick={close}
            />
          ))}
      </AppShell.Navbar>

      <AppShell.Main>
        {navigation.state === "loading" && (
          <div className={classes.loading} role="progressbar" aria-label="Loading the page" />
        )}
        <Outlet />
      </AppShell.Main>
    </AppShell>
  );
}

/** Firing alerts at a glance; each chip opens the alert list. */
function AlertChips() {
  const counts = useAlertCounts();
  if (!counts.data) return null;

  const { critical, warning } = counts.data;
  if (critical === 0 && warning === 0) {
    return (
      <Badge color="healthy" leftSection={<IconCircleCheck size={13} />} visibleFrom="xs">
        All clear
      </Badge>
    );
  }

  return (
    <Group gap={6} wrap="nowrap">
      {critical > 0 && (
        <Badge color="crimson" variant="filled" component={Link} to="/alerts?severity=Critical">
          {critical} critical
        </Badge>
      )}
      {warning > 0 && (
        <Badge color="bronze" component={Link} to="/alerts?severity=Warning">
          {warning} warning
        </Badge>
      )}
    </Group>
  );
}

const colorSchemes: { value: MantineColorScheme; label: string; icon: typeof IconSun }[] = [
  { value: "light", label: "Light", icon: IconSun },
  { value: "dark", label: "Dark", icon: IconMoon },
  { value: "auto", label: "Match the system", icon: IconDeviceDesktopAnalytics },
];

function UserMenu() {
  const me = useCurrentUser();
  const server = useServerInfo();
  const update = useServerUpdate(me.data?.isAdmin === true);
  const [updatesOpen, updates] = useDisclosure(false);
  const logout = useLogout();
  const navigate = useNavigate();
  const { colorScheme, setColorScheme } = useMantineColorScheme();
  const name = me.data?.displayName || me.data?.email || "";

  return (
    <>
      <Menu position="bottom-end" width={220}>
        <Menu.Target>
          <UnstyledButton aria-label="Account menu">
            <Group gap={8} wrap="nowrap">
              <Avatar size={28} radius="xl" color="iris" name={name} />
              <Text fz="sm" fw={500} visibleFrom="sm">
                {name}
              </Text>
            </Group>
          </UnstyledButton>
        </Menu.Target>
        <Menu.Dropdown>
          <Menu.Label>{me.data?.email}</Menu.Label>
          <Menu.Item component={Link} to="/account" leftSection={<IconUserCircle size={16} />}>
            Account
          </Menu.Item>
          {me.data?.isAdmin && update.data && (
            <Menu.Item leftSection={<IconArrowUpCircle size={16} />} onClick={updates.open}>
              Updates
            </Menu.Item>
          )}
          <Menu.Divider />
          <Menu.Label>Appearance</Menu.Label>
          {colorSchemes.map((scheme) => (
            <Menu.Item
              key={scheme.value}
              leftSection={<scheme.icon size={16} />}
              onClick={() => setColorScheme(scheme.value)}
              rightSection={colorScheme === scheme.value ? <IconCircleCheck size={14} /> : undefined}
            >
              {scheme.label}
            </Menu.Item>
          ))}
          <Menu.Divider />
          <Menu.Item
            leftSection={<IconLogout size={16} />}
            onClick={() =>
              logout.mutate(undefined, { onSettled: () => void navigate("/login", { replace: true }) })
            }
          >
            Sign out
          </Menu.Item>
          {server.data && <Menu.Label>Argus {server.data.version}</Menu.Label>}
        </Menu.Dropdown>
      </Menu>
      {update.data && <ServerUpdateModal info={update.data} opened={updatesOpen} onClose={updates.close} />}
    </>
  );
}
