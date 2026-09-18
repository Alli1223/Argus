import { screen, waitFor } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { EmailSettings } from "../../api/types";
import { mockApi, reply } from "../../test/api";
import { renderWithApp } from "../../test/render";
import { EmailSettingsSection } from "./EmailSettingsSection";

const settings = (overrides: Partial<EmailSettings> = {}): EmailSettings => ({
  configured: true,
  source: "App",
  host: "smtp.gmail.com",
  port: 587,
  security: "Auto",
  username: "me@gmail.com",
  hasPassword: true,
  from: "me@gmail.com",
  fromName: "Argus",
  updatedAt: "2026-09-18T09:00:00Z",
  updatedBy: "admin@example.com",
  ...overrides,
});

const me = { id: "u1", email: "admin@example.com", displayName: "Admin", role: "Admin" };

describe("EmailSettingsSection", () => {
  it("says when no mail server is set and saves the one entered", async () => {
    const saved = settings({ host: "smtp.gmail.com", username: "me@gmail.com" });
    const calls = mockApi({
      "GET /api/settings/email": settings({
        configured: false,
        source: "None",
        host: null,
        from: null,
        username: null,
        hasPassword: false,
        updatedAt: null,
        updatedBy: null,
      }),
      "GET /api/auth/me": me,
      "PUT /api/settings/email": saved,
    });
    const { user } = renderWithApp(<EmailSettingsSection />);

    expect(await screen.findByText("Argus cannot send email yet")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Send test" })).toBeDisabled();

    await user.type(screen.getByLabelText(/Mail server/), "smtp.x");
    await user.type(screen.getByLabelText("Password"), "pw");
    await user.type(screen.getByLabelText(/From address/), "a@x.io");
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(calls.some((call) => call.method === "PUT")).toBe(true));
    expect(calls.find((call) => call.method === "PUT")?.body).toMatchObject({
      host: "smtp.x",
      port: 587,
      security: "Auto",
      username: null,
      password: "pw",
      from: "a@x.io",
    });
    expect(await screen.findByText("Email settings saved.")).toBeInTheDocument();
  });

  it("keeps the saved password when the box is left alone", async () => {
    const calls = mockApi({
      "GET /api/settings/email": settings(),
      "GET /api/auth/me": me,
      "PUT /api/settings/email": settings({ host: "smtp.b.test" }),
    });
    const { user } = renderWithApp(<EmailSettingsSection />);

    const host = await screen.findByLabelText(/Mail server/);
    await user.clear(host);
    await user.type(host, "smtp.b.test");
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(calls.some((call) => call.method === "PUT")).toBe(true));
    const sent = calls.find((call) => call.method === "PUT")?.body as Record<string, unknown>;
    expect(sent.host).toBe("smtp.b.test");
    expect(sent).not.toHaveProperty("password");
  });

  it("notes settings that come from the Compose file", async () => {
    mockApi({
      "GET /api/settings/email": settings({ source: "File", updatedAt: null, updatedBy: null }),
      "GET /api/auth/me": me,
    });
    renderWithApp(<EmailSettingsSection />);

    expect(await screen.findByText("These settings come from the Compose file")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Forget these settings" })).not.toBeInTheDocument();
  });

  it("sends a test email and shows what the mail server said", async () => {
    const calls = mockApi({
      "GET /api/settings/email": settings(),
      "GET /api/auth/me": me,
      "POST /api/settings/email/test": reply(502, {
        title: "The test was not sent",
        detail: "535 5.7.8 Username and Password not accepted",
      }),
    });
    const { user } = renderWithApp(<EmailSettingsSection />);

    await user.click(await screen.findByRole("button", { name: "Send test" }));

    await waitFor(() => expect(calls.some((call) => call.path === "/api/settings/email/test")).toBe(true));
    expect(calls.find((call) => call.path === "/api/settings/email/test")?.body).toEqual({
      to: "admin@example.com",
    });
    expect(await screen.findByText("535 5.7.8 Username and Password not accepted")).toBeInTheDocument();
  });
});
