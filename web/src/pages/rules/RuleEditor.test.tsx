import { screen, waitFor } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { AlertRule } from "../../api/types";
import { mockApi } from "../../test/api";
import { renderWithApp } from "../../test/render";
import { RuleEditor } from "./RuleEditor";

const diskRule: AlertRule = {
  id: "r1",
  name: "Disk filling up",
  metric: "DiskUsage",
  condition: "Threshold",
  operator: "Above",
  threshold: 90,
  durationSeconds: 300,
  severity: "Warning",
  hostId: null,
  hostName: null,
  tag: null,
  resourceFilter: "/var",
  enabled: true,
  firingAlerts: 0,
  createdAt: "2026-09-16T12:00:00Z",
  updatedAt: "2026-09-16T12:00:00Z",
};

describe("RuleEditor", () => {
  it("compares host-wide metrics with their usual level", async () => {
    const calls = mockApi({
      "GET /api/hosts": [],
      "POST /api/alert-rules": (body) => ({ ...diskRule, ...(body as object), id: "r2" }),
    });
    const { user } = renderWithApp(<RuleEditor rule={null} opened onClose={() => {}} />);

    await user.type(screen.getByLabelText(/^Name/), "Odd CPU");
    await user.click(screen.getByRole("radio", { name: "Its usual level" }));

    expect(screen.getByLabelText("Sensitivity")).toHaveValue("3 σ");
    // Five minutes, the default, is already long enough for an anomaly rule.
    expect(screen.getByLabelText("For at least")).toHaveValue("5 min");
    expect(screen.getByRole("radio", { name: "Higher than usual" })).toBeChecked();

    await user.click(screen.getByRole("button", { name: "Create rule" }));

    await waitFor(() =>
      expect(calls.find((call) => call.method === "POST")?.body).toMatchObject({
        metric: "CpuUsage",
        condition: "Anomaly",
        operator: "Above",
        threshold: 3,
        durationSeconds: 300,
      }),
    );
  });

  it("counts a container's restarts over a window", async () => {
    const calls = mockApi({
      "GET /api/hosts": [],
      "POST /api/alert-rules": (body) => ({ ...diskRule, ...(body as object), id: "r3" }),
    });
    const { user } = renderWithApp(<RuleEditor rule={null} opened onClose={() => {}} />);

    await user.type(screen.getByLabelText(/^Name/), "Worker loop");
    await user.click(screen.getByLabelText("Watch", { selector: "input" }));
    await user.click(await screen.findByRole("option", { name: "Container restarts" }));

    expect(screen.getByLabelText("Fires when Docker restarts a container more than")).toHaveValue("3 times");
    expect(screen.getByLabelText("Within")).toHaveValue("5 min");
    expect(screen.queryByRole("radio", { name: "Below" })).not.toBeInTheDocument();
    await user.type(screen.getByLabelText(/^Container/), "worker");
    await user.click(screen.getByRole("button", { name: "Create rule" }));

    await waitFor(() =>
      expect(calls.find((call) => call.method === "POST")?.body).toMatchObject({
        metric: "ContainerRestarts",
        operator: "Above",
        threshold: 3,
        durationSeconds: 300,
        resourceFilter: "worker",
      }),
    );
  });

  it("keeps disk rules to fixed thresholds", async () => {
    mockApi({ "GET /api/hosts": [] });
    renderWithApp(<RuleEditor rule={diskRule} opened onClose={() => {}} />);

    expect(screen.getByLabelText(/^Mount point/)).toHaveValue("/var");
    expect(screen.getByLabelText("Threshold")).toHaveValue("90%");
    expect(screen.queryByRole("radio", { name: "Its usual level" })).not.toBeInTheDocument();
  });
});
