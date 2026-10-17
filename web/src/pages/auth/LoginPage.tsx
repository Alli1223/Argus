import { Alert, Anchor, Button, Checkbox, PasswordInput, Stack, Text, TextInput } from "@mantine/core";
import { isEmail, isNotEmpty, useForm } from "@mantine/form";
import { Link, Navigate, useNavigate, useSearchParams } from "react-router";
import { useState } from "react";
import {
  isTwoFactorRequired,
  safeNextPath,
  useAuthStatus,
  useLogin,
  useLoginTwoFactor,
} from "../../api/auth";
import type { Credentials } from "../../api/types";
import { AuthPanel } from "../../app/AuthLayout";

export function LoginPage() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const status = useAuthStatus();
  const login = useLogin();
  const [codeStep, setCodeStep] = useState(false);
  const form = useForm<Credentials>({
    initialValues: { email: "", password: "", rememberMe: true },
    validate: {
      email: isEmail("Enter the email address you signed up with"),
      password: isNotEmpty("Enter your password"),
    },
  });

  if (status.data?.setupRequired) return <Navigate to="/setup" replace />;

  const done = () => void navigate(safeNextPath(params.get("next")), { replace: true });
  const submit = form.onSubmit((values) =>
    login.mutate(values, {
      onSuccess: (result) => (isTwoFactorRequired(result) ? setCodeStep(true) : done()),
    }),
  );

  if (codeStep) {
    return (
      <CodeStep
        rememberMe={form.values.rememberMe}
        onSignedIn={done}
        onBack={() => {
          form.setFieldValue("password", "");
          setCodeStep(false);
        }}
      />
    );
  }

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

/** The second step for accounts with two-factor sign-in: a code from the app, or a recovery code. */
function CodeStep({
  rememberMe,
  onSignedIn,
  onBack,
}: {
  rememberMe: boolean;
  onSignedIn: () => void;
  onBack: () => void;
}) {
  const verify = useLoginTwoFactor();
  const [recovery, setRecovery] = useState(false);
  const [code, setCode] = useState("");

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    if (code.trim() === "") return;
    verify.mutate({ code: code.trim(), rememberMe }, { onSuccess: onSignedIn });
  };

  // An expired password step cannot be finished with any code; start again.
  const expired = verify.error?.title === "Sign in again";

  return (
    <AuthPanel
      title="Two-factor sign-in"
      description={
        recovery
          ? "Enter one of the recovery codes you saved when you set up two-factor sign-in. Each works once."
          : "Enter the 6-digit code from your authenticator app."
      }
    >
      <form onSubmit={submit} noValidate>
        <Stack gap="md">
          <TextInput
            key={recovery ? "recovery" : "app"}
            label={recovery ? "Recovery code" : "Code"}
            placeholder={recovery ? "XXXXX-XXXXX" : "123 456"}
            autoComplete="one-time-code"
            inputMode={recovery ? "text" : "numeric"}
            autoFocus
            value={code}
            onChange={(event) => setCode(event.currentTarget.value)}
          />
          {verify.error && (
            <Alert color="crimson" title={verify.error.title}>
              {verify.error.message}
            </Alert>
          )}
          {expired ? (
            <Button onClick={onBack}>Enter the password again</Button>
          ) : (
            <Button type="submit" loading={verify.isPending} disabled={code.trim() === ""}>
              Sign in
            </Button>
          )}
        </Stack>
      </form>
      <Text fz="sm" c="dimmed">
        <Anchor
          component="button"
          type="button"
          fz="sm"
          onClick={() => {
            setRecovery(!recovery);
            setCode("");
            verify.reset();
          }}
        >
          {recovery ? "Use the authenticator app instead" : "Lost your phone? Use a recovery code"}
        </Anchor>
        {" · "}
        <Anchor component="button" type="button" fz="sm" onClick={onBack}>
          Back
        </Anchor>
      </Text>
    </AuthPanel>
  );
}
