import { vi } from "vitest";

export interface ApiCall {
  method: string;
  path: string;
  body: unknown;
}

/** A reply with a status other than 200. */
export class Reply {
  readonly status: number;
  readonly body: unknown;

  constructor(status: number, body?: unknown) {
    this.status = status;
    this.body = body;
  }
}

export const reply = (status: number, body?: unknown) => new Reply(status, body);

type Route = object | string | number | boolean | null | ((body: unknown) => unknown);

/**
 * Answers the app's API calls from a table keyed "METHOD /path", with data (sent as 200), a
 * {@link Reply}, or a function of the request body returning either. Returns every call made.
 */
export function mockApi(routes: Record<string, Route>): ApiCall[] {
  const calls: ApiCall[] = [];
  vi.stubGlobal("fetch", async (input: RequestInfo | URL, init?: RequestInit) => {
    const method = init?.method ?? "GET";
    const path = String(input);
    const body = typeof init?.body === "string" ? JSON.parse(init.body) : undefined;
    calls.push({ method, path, body });

    const key = `${method} ${path}`;
    if (!(key in routes)) {
      return Response.json({ title: `No mock for ${key}` }, { status: 500 });
    }

    const route = routes[key];
    const result = typeof route === "function" ? route(body) : route;
    const { status, data } =
      result instanceof Reply ? { status: result.status, data: result.body } : { status: 200, data: result };
    return data === undefined ? new Response(null, { status }) : Response.json(data, { status });
  });
  return calls;
}
