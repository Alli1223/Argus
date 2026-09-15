import { Alert, Anchor, Button, Stack, Text } from "@mantine/core";
import { useForm } from "@mantine/form";
import { Link, Navigate, useNavigate } from "react-router";
import { useAuthStatus, useRegister } from "../../api/auth";
import type { NewAccount } from "../../api/types";
import { AuthPanel } from "../../app/AuthLayout";
import { FullPageLoader } from "../../components/Screens";
import { AccountFields } from "./AccountFields";
import { newAccountValidation } from "./accountValidation";

export function RegisterPage() {
  const navigate = useNavigate();
  const status = useAuthStatus();
  const register = useRegister();
  const form = useForm<NewAccount>({
    initialValues: { displayName: "", email: "", password: "" },
    validate: newAccountValidation,
  });

  if (status.isPending) return <FullPageLoader />;
  if (status.data?.setupRequired) return <Navigate to="/setup" replace />;

  if (!status.data?.registrationEnabled) {
    return (
      <AuthPanel
        title="Registration is closed"
        description="Ask an administrator of this Argus server to create an account for you."
      >
        <Anchor component={Link} to="/login">
          Back to sign in
        </Anchor>
      </AuthPanel>
    );
  }

  const submit = form.onSubmit((values) =>
    register.mutate(values, {
      onSuccess: () => void navigate("/systems", { replace: true }),
      onError: (error) => form.setErrors(error.fieldErrors),
    }),
  );

  return (
    <AuthPanel title="Create an account" description="Then add your first system to start watching it.">
      <form onSubmit={submit} noValidate>
        <Stack gap="md">
          <AccountFields form={form} />
          {register.error && Object.keys(register.error.fieldErrors).length === 0 && (
            <Alert color="crimson" title={register.error.title}>
              {register.error.message}
            </Alert>
          )}
          <Button type="submit" loading={register.isPending}>
            Create account
          </Button>
        </Stack>
      </form>
      <Text fz="sm" c="dimmed">
        Already have an account?{" "}
        <Anchor component={Link} to="/login" fz="sm">
          Sign in
        </Anchor>
      </Text>
    </AuthPanel>
  );
}
