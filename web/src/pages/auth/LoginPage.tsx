import { Alert, Anchor, Button, Checkbox, PasswordInput, Stack, Text, TextInput } from "@mantine/core";
import { isEmail, isNotEmpty, useForm } from "@mantine/form";
import { Link, Navigate, useNavigate, useSearchParams } from "react-router";
import { safeNextPath, useAuthStatus, useLogin } from "../../api/auth";
import type { Credentials } from "../../api/types";
import { AuthPanel } from "../../app/AuthLayout";

export function LoginPage() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const status = useAuthStatus();
  const login = useLogin();
  const form = useForm<Credentials>({
    initialValues: { email: "", password: "", rememberMe: true },
    validate: {
      email: isEmail("Enter the email address you signed up with"),
      password: isNotEmpty("Enter your password"),
    },
  });

  if (status.data?.setupRequired) return <Navigate to="/setup" replace />;

  const submit = form.onSubmit((values) =>
    login.mutate(values, {
      onSuccess: () => void navigate(safeNextPath(params.get("next")), { replace: true }),
    }),
  );

  return (
    <AuthPanel title="Sign in" description="See how your systems are doing.">
      <form onSubmit={submit} noValidate>
        <Stack gap="md">
          <TextInput
            label="Email"
            type="email"
            autoComplete="username"
            autoFocus
            {...form.getInputProps("email")}
          />
          <PasswordInput
            label="Password"
            autoComplete="current-password"
            {...form.getInputProps("password")}
          />
          <Checkbox
            label="Keep me signed in on this device"
            {...form.getInputProps("rememberMe", { type: "checkbox" })}
          />
          {login.error && (
            <Alert color="crimson" title={login.error.title}>
              {login.error.message}
            </Alert>
          )}
          <Button type="submit" loading={login.isPending}>
            Sign in
          </Button>
        </Stack>
      </form>
      {status.data?.registrationEnabled && (
        <Text fz="sm" c="dimmed">
          No account yet?{" "}
          <Anchor component={Link} to="/register" fz="sm">
            Create one
          </Anchor>
        </Text>
      )}
    </AuthPanel>
  );
}
