import { screen, waitFor } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { mockApi, reply } from "../../test/api";
import { renderWithApp } from "../../test/render";
import { LoginPage } from "./LoginPage";

const me = {
  id: "u1",
  email: "ada@example.com",
  displayName: "Ada",
  roles: ["User"],
  isAdmin: false,
  twoFactorEnabled: true,
};

describe("LoginPage", () => {
  it("asks for the authenticator code when the account has two-factor sign-in", async () => {
    const calls = mockApi({
      "GET /api/auth/status": { setupRequired: false, registrationEnabled: false },
      "POST /api/auth/login": reply(202, { twoFactorRequired: true }),
      "POST /api/auth/login/two-factor": (body) =>
        (body as { code: string }).code === "123 456"
          ? me
          : reply(401, { title: "Wrong code", detail: "Not right." }),
    });
    const { user } = renderWithApp(<LoginPage />);

    await user.type(screen.getByLabelText("Email"), "ada@example.com");
    await user.type(screen.getByLabelText("Password"), "correct horse battery");
    await user.click(screen.getByRole("button", { name: "Sign in" }));

    const code = await screen.findByLabelText("Code");
    await user.type(code, "000000");
    await user.click(screen.getByRole("button", { name: "Sign in" }));
    expect(await screen.findByText("Wrong code")).toBeInTheDocument();

    await user.clear(code);
    await user.type(code, "123 456");
    await user.click(screen.getByRole("button", { name: "Sign in" }));

    await waitFor(() =>
      expect(calls.filter((call) => call.path === "/api/auth/login/two-factor").at(-1)?.body).toEqual({
        code: "123 456",
        rememberMe: true,
      }),
    );
  });

  it("switches to a recovery code", async () => {
    mockApi({
      "GET /api/auth/status": { setupRequired: false, registrationEnabled: false },
      "POST /api/auth/login": reply(202, { twoFactorRequired: true }),
    });
    const { user } = renderWithApp(<LoginPage />);

    await user.type(screen.getByLabelText("Email"), "ada@example.com");
    await user.type(screen.getByLabelText("Password"), "correct horse battery");
    await user.click(screen.getByRole("button", { name: "Sign in" }));
    await user.click(await screen.findByRole("button", { name: /Use a recovery code/ }));

    expect(screen.getByLabelText("Recovery code")).toBeInTheDocument();
  });
});
