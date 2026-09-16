import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { mockApi } from "../../test/api";
import { renderWithApp } from "../../test/render";
import { ServicesSection } from "./HostResources";

const now = Date.parse("2026-09-16T12:00:00Z");

describe("ServicesSection", () => {
  it("lists failing services with how long they have been down", async () => {
    mockApi({
      "GET /api/hosts/h1/services": {
        checkedAt: "2026-09-16T11:59:30Z",
        failures: [
          {
            service: "nginx.service",
            description: "A web server",
            state: "failed",
            since: "2026-09-16T09:45:00Z",
          },
        ],
      },
    });
    renderWithApp(<ServicesSection hostId="h1" now={now} />);

    expect(await screen.findByText("nginx.service")).toBeInTheDocument();
    expect(screen.getByText("A web server")).toBeInTheDocument();
    expect(screen.getByText("1 service failing, checked 30s ago.")).toBeInTheDocument();
    expect(screen.getByText("2h 15m")).toBeInTheDocument();
  });

  it("says so when every service is running", async () => {
    mockApi({ "GET /api/hosts/h1/services": { checkedAt: "2026-09-16T11:59:30Z", failures: [] } });
    renderWithApp(<ServicesSection hostId="h1" now={now} />);

    expect(await screen.findByText("Every service the agent watches is running.")).toBeInTheDocument();
  });

  it("explains that nothing has been checked yet", async () => {
    mockApi({ "GET /api/hosts/h1/services": { checkedAt: null, failures: [] } });
    renderWithApp(<ServicesSection hostId="h1" now={now} />);

    expect(await screen.findByText(/No check has arrived from this one yet/)).toBeInTheDocument();
  });
});
