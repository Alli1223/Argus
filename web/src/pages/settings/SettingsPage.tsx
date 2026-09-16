import { PageHeader } from "../../components/PageHeader";
import { ServerUpdatesSection } from "./ServerUpdatesSection";

/** Settings for the Argus server as a whole, for administrators. */
export function SettingsPage() {
  return (
    <>
      <PageHeader title="Settings" description="The Argus server itself." />
      <ServerUpdatesSection />
    </>
  );
}
