import { PageHeader } from "../components/PageHeader";

// Sections that are built out in the following steps of the TODO list.

export function OverviewPage() {
  return <PageHeader title="Overview" description="Every system you watch, worst first." />;
}

export function HostsPage() {
  return <PageHeader title="Hosts" description="All machines reporting to this server." />;
}

export function HostPage() {
  return <PageHeader title="Host" />;
}

export function AlertsPage() {
  return <PageHeader title="Alerts" description="What needs attention now, and what happened before." />;
}

export function RulesPage() {
  return <PageHeader title="Alert rules" description="When Argus should raise an alert." />;
}

export function SystemsPage() {
  return (
    <PageHeader title="Add a system" description="Install the agent on a machine to start watching it." />
  );
}

export function UsersPage() {
  return <PageHeader title="Users" description="People who can sign in to this server." />;
}

export function AccountPage() {
  return <PageHeader title="Account" />;
}
