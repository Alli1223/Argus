import { screen, waitFor } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { ApiTokenSummary } from "../../api/types";
import { mockApi, reply } from "../../test/api";
import { renderWithApp } from "../../test/render";
import { ApiTokensPanel } from "./ApiTokensPanel";

const token = (overrides: Partial<ApiTokenSummary> = {}): ApiTokenSummary => ({
  id: "t1",
  name: "Home Assistant",
  tokenPrefix: "argus_at_Ab12…",
  createdAt: "2026-10-05T09:00:00Z",
  lastUsedAt: null,
  revokedAt: null,
  isActive: true,
  ...overrides,
});

describe("ApiTokensPanel", () => {
  it("shows a new token once, with its name", async () => {
    const secret = "argus_at_" + "x".repeat(43);
    const calls = mockApi({
      "GET /api/account/api-tokens": [],
      "POST /api/account/api-tokens": { token: secret, summary: token() },
    });
    const { user } = renderWithApp(<ApiTokensPanel />);

    expect(await screen.findByText("No tokens yet.")).toBeInTheDocument();
    await user.type(screen.getByLabelText("Token name"), "Home Assistant");
    await user.click(screen.getByRole("button", { name: "Create token" }));

    expect(await screen.findByText(secret)).toBeInTheDocument();
    expect(calls.find((call) => call.method === "POST")?.body).toEqual({ name: "Home Assistant" });

    await user.click(screen.getByRole("button", { name: "Done" }));
    expect(screen.queryByText(secret)).not.toBeInTheDocument();
  }, 15_000);

  it("asks for a name", async () => {
    const calls = mockApi({ "GET /api/account/api-tokens": [] });
    const { user } = renderWithApp(<ApiTokensPanel />);

    await user.click(await screen.findByRole("button", { name: "Create token" }));

    expect(await screen.findByText(/Name it after what will use it/)).toBeInTheDocument();
    expect(calls.some((call) => call.method === "POST")).toBe(false);
  });

  it("revokes a token after asking", async () => {
    const calls = mockApi({
      "GET /api/account/api-tokens": [
        token(),
        token({ id: "t2", name: "Old", isActive: false, revokedAt: "2026-10-01T00:00:00Z" }),
      ],
      "DELETE /api/account/api-tokens/t1": reply(204),
    });
    const { user } = renderWithApp(<ApiTokensPanel />);

    expect(await screen.findByText("Revoked")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Revoke" }));
    await user.click(await screen.findByRole("button", { name: "Revoke token" }));

    await waitFor(() => expect(calls.some((call) => call.method === "DELETE")).toBe(true));
    expect(await screen.findByText("Home Assistant revoked.")).toBeInTheDocument();
  });
});
