import { hasLength, isEmail, isNotEmpty } from "@mantine/form";

/** Matches the server's rule (ASP.NET Core Identity options). */
export const PASSWORD_MIN_LENGTH = 10;

export const newAccountValidation = {
  displayName: isNotEmpty("Enter a name"),
  email: isEmail("Enter a valid email address"),
  password: hasLength({ min: PASSWORD_MIN_LENGTH }, `Use at least ${PASSWORD_MIN_LENGTH} characters`),
};
