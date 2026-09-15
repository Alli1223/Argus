import { describe, expect, it } from "vitest";
import { viewFromParams, viewToParams } from "./alertFilters";

describe("viewFromParams", () => {
  it("defaults to firing alerts on the first page", () => {
    expect(viewFromParams(new URLSearchParams(""))).toEqual({
      status: "Firing",
      severity: null,
      hostId: null,
      page: 1,
    });
  });

  it("reads filters and ignores anything unknown", () => {
    expect(viewFromParams(new URLSearchParams("status=Resolved&severity=Critical&host=h1&page=3"))).toEqual({
      status: "Resolved",
      severity: "Critical",
      hostId: "h1",
      page: 3,
    });
    expect(viewFromParams(new URLSearchParams("status=nope&severity=Loud&page=-2"))).toEqual({
      status: "Firing",
      severity: null,
      hostId: null,
      page: 1,
    });
  });
});

describe("viewToParams", () => {
  it("writes only what differs from the default", () => {
    expect(viewToParams({ status: "Firing", severity: null, hostId: null, page: 1 })).toEqual({});
    expect(viewToParams({ status: "all", severity: "Warning", hostId: "h1", page: 2 })).toEqual({
      status: "all",
      severity: "Warning",
      host: "h1",
      page: "2",
    });
  });
});
