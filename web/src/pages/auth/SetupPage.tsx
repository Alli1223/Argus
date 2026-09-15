import { Alert, Button, Stack } from "@mantine/core";
import { useForm } from "@mantine/form";
import { Navigate, useNavigate } from "react-router";
import { useAuthStatus, useSetup } from "../../api/auth";
import type { NewAccount } from "../../api/types";
import { AuthPanel } from "../../app/AuthLayout";
import { FullPageLoader } from "../../components/Screens";
import { AccountFields } from "./AccountFields";
import { newAccountValidation } from "./accountValidation";

export function SetupPage() {
  const navigate = useNavigate();
  const status = useAuthStatus();
  const setup = useSetup();
  const form = useForm<NewAccount>({
    initialValues: { displayName: "", email: "", password: "" },
    validate: newAccountValidation,
  });

  if (status.isPending) return <FullPageLoader />;
  if (status.data && !status.data.setupRequired) return <Navigate to="/login" replace />;

  const submit = form.onSubmit((values) =>
    setup.mutate(values, {
      onSuccess: () => void navigate("/systems", { replace: true }),
      onError: (error) => form.setErrors(error.fieldErrors),
    }),
  );

  return (
    <AuthPanel
      title="Set up Argus"
      description="Create the administrator account. It can add people later and sees every system."
    >
      <form onSubmit={submit} noValidate>
        <Stack gap="md">
          <AccountFields form={form} />
          {setup.error && Object.keys(setup.error.fieldErrors).length === 0 && (
            <Alert color="crimson" title={setup.error.title}>
              {setup.error.message}
            </Alert>
          )}
          <Button type="submit" loading={setup.isPending}>
            Create account
          </Button>
        </Stack>
      </form>
    </AuthPanel>
  );
}
