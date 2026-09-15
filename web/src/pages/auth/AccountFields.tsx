import { PasswordInput, TextInput } from "@mantine/core";
import type { UseFormReturnType } from "@mantine/form";
import type { NewAccount } from "../../api/types";
import { PASSWORD_MIN_LENGTH } from "./accountValidation";

/** Name, email and password fields shared by first-run setup and registration. */
export function AccountFields({ form }: { form: UseFormReturnType<NewAccount> }) {
  return (
    <>
      <TextInput label="Name" autoComplete="name" autoFocus {...form.getInputProps("displayName")} />
      <TextInput label="Email" type="email" autoComplete="username" {...form.getInputProps("email")} />
      <PasswordInput
        label="Password"
        description={`At least ${PASSWORD_MIN_LENGTH} characters. A few unrelated words make a strong one.`}
        autoComplete="new-password"
        {...form.getInputProps("password")}
      />
    </>
  );
}
