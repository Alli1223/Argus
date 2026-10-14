import { screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { mockApi } from "../../test/api";
import { renderWithApp } from "../../test/render";
import { UpsSection } from "./HostResources";

const now = Date.parse("2026-10-14T10:40:00Z");

describe("UpsSection", () => {
  it("shows each UPS's state, charge, runtime and load", async () => {
    mockApi({
      "GET /api/hosts/h1/ups": [
        {
          name: "ups",
          model: "APC Back-UPS ES 700G",
          status: "OB DISCHRG",
          onBattery: true,
          lowBattery: false,
          batteryChargePercent: 16,
          batteryRuntimeSeconds: 420,
          loadPercent: 23,
          inputVoltage: 0,
          outputVoltage: 230,
          batteryVoltage: 12.1,
          time: "2026-10-14T10:39:45Z",
        },
      ],
    });
    renderWithApp(<UpsSection hostId="h1" now={now} />);

    expect(await screen.findByText("APC Back-UPS ES 700G")).toBeInTheDocument();
    expect(screen.getByText("On battery")).toBeInTheDocument();
    expect(screen.getByText("discharging")).toBeInTheDocument();
    expect(screen.getByRole("meter", { name: "ups battery charge" })).toHaveAttribute("aria-valuenow", "16");
    expect(screen.getByText("7m")).toBeInTheDocument();
    expect(screen.getByText("230 V")).toBeInTheDocument();
    expect(screen.getByText("As of 15s ago")).toBeInTheDocument();
  });

  it("is left out on hosts without a UPS", async () => {
    const calls = mockApi({ "GET /api/hosts/h1/ups": [] });
    renderWithApp(<UpsSection hostId="h1" now={now} />);

    await vi.waitFor(() => expect(calls).toHaveLength(1));
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
    expect(screen.queryByText("UPS")).not.toBeInTheDocument();
  });
});
