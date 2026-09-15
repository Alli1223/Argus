/** Problem details (RFC 9457), the shape of every error the server returns. */
interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}

const CSRF_HEADER = "X-Argus-Csrf";

function defaultTitle(status: number): string {
  if (status === 401) return "You are signed out";
  if (status === 403) return "Not allowed";
  if (status === 404) return "Not found";
  if (status === 429) return "Too many attempts";
  if (status >= 500) return "The server ran into a problem";
  return "Request failed";
}

function camelCase(key: string): string {
  return key.length > 0 ? key[0].toLowerCase() + key.slice(1) : key;
}

export class ApiError extends Error {
  readonly status: number;
  readonly title: string;
  readonly errors: Record<string, string[]>;

  constructor(status: number, problem: ProblemDetails | undefined) {
    const title = problem?.title ?? defaultTitle(status);
    super(problem?.detail ?? title);
    this.name = "ApiError";
    this.status = status;
    this.title = title;
    this.errors = problem?.errors ?? {};
  }

  /** The first message for each field, keyed the way forms name their fields. */
  get fieldErrors(): Record<string, string> {
    return Object.fromEntries(
      Object.entries(this.errors)
        .filter(([, messages]) => messages.length > 0)
        .map(([key, messages]) => [camelCase(key), messages[0]]),
    );
  }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  let response: Response;
  try {
    response = await fetch(path, {
      method,
      credentials: "same-origin",
      headers: {
        Accept: "application/json",
        // Required on state-changing calls; browsers cannot add it to forged cross-site requests.
        [CSRF_HEADER]: "1",
        ...(body === undefined ? {} : { "Content-Type": "application/json" }),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    throw new ApiError(0, {
      title: "Can't reach the Argus server",
      detail: "Check your connection, then try again.",
    });
  }

  const text = await response.text();
  let data: unknown;
  try {
    data = text ? JSON.parse(text) : undefined;
  } catch {
    data = undefined;
  }

  if (!response.ok) {
    throw new ApiError(response.status, data as ProblemDetails | undefined);
  }

  return data as T;
}

export const api = {
  get: <T>(path: string) => request<T>("GET", path),
  post: <T = void>(path: string, body?: unknown) => request<T>("POST", path, body),
  put: <T>(path: string, body: unknown) => request<T>("PUT", path, body),
  patch: <T>(path: string, body: unknown) => request<T>("PATCH", path, body),
  delete: (path: string) => request<void>("DELETE", path),
};

/** Builds a query string from the values that are set. */
export function query(params: Record<string, string | number | boolean | null | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== null && value !== undefined && value !== "") {
      search.set(key, String(value));
    }
  }
  const text = search.toString();
  return text ? `?${text}` : "";
}
