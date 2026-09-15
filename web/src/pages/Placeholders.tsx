import { PageHeader } from "../components/PageHeader";

// Sections that are built out in the following steps of the TODO list.

export function UsersPage() {
  return <PageHeader title="Users" description="People who can sign in to this server." />;
}

export function AccountPage() {
  return <PageHeader title="Account" />;
}
