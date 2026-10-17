import { PageHeader } from "../../components/PageHeader";
import { EmailSettingsSection } from "./EmailSettingsSection";
import { ServerUpdatesSection } from "./ServerUpdatesSection";
import { TelegramSettingsSection } from "./TelegramSettingsSection";

/** Settings for the Argus server as a whole, for administrators. */
export function SettingsPage() {
  return (
    <>
      <PageHeader title="Settings" description="The Argus server itself." />
      <ServerUpdatesSection />
      <EmailSettingsSection />
      <TelegramSettingsSection />
    </>
  );
}
