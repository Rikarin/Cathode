import createClient from 'openapi-fetch';
import type { paths } from '../generated/schema';

/** Where a Server is and how to authenticate to it. */
export interface Connection {
  /** e.g. `http://127.0.0.1:5180`, without a trailing slash. */
  baseUrl: string;
  /** Bearer token; null for a development Server started without one. */
  token: string | null;
}

/** An error reply, with the Server's ProblemDetails text when it sent one. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
    readonly errors: readonly string[] = []
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

/**
 * A typed client for REST API v1, generated from the Server's committed OpenAPI document. A reply that is not 2xx
 * rejects with an {@link ApiError} carrying the Server's reason, so a call cannot report success for a refused request
 * because its caller forgot to look ({@link unwrap} only narrows the type of a successful one).
 */
export function createCathodeClient(connection: Connection, fetchImpl: typeof fetch = globalThis.fetch) {
  const client = createClient<paths>({
    baseUrl: connection.baseUrl,
    fetch: (request: Request) => fetchImpl(request)
  });
  client.use({
    async onResponse({ response }) {
      if (!response.ok) {
        throw await problemError(response);
      }

      return undefined;
    }
  });
  if (connection.token) {
    const token = connection.token;
    client.use({
      onRequest({ request }) {
        request.headers.set('Authorization', `Bearer ${token}`);
        return request;
      }
    });
  }

  return client;
}

export type CathodeClient = ReturnType<typeof createCathodeClient>;

interface Result<T> {
  data?: T;
  error?: unknown;
  response: Response;
}

/** Returns the data of a successful call, or throws an {@link ApiError}. */
export function unwrap<T>(result: Result<T>): T {
  if (result.response.ok) {
    return result.data as T;
  }

  throw toApiError(result.response, result.error);
}

/** The {@link ApiError} for a reply that is not 2xx, with its ProblemDetails when the body is one. */
export async function problemError(response: Response): Promise<ApiError> {
  let body: unknown;
  try {
    const text = await response.clone().text();
    body = text ? JSON.parse(text) : undefined;
  } catch {
    body = undefined; // not JSON: the status says it
  }

  return toApiError(response, body);
}

function toApiError(response: Response, error: unknown): ApiError {
  const problem = (error !== null && typeof error === 'object' ? error : {}) as {
    detail?: string;
    title?: string;
    errors?: unknown;
  };
  const errors = Array.isArray(problem.errors) ? problem.errors.map(String) : [];
  const text = typeof error === 'string' && error.trim() ? error : undefined; // a plain BadRequest<string>
  return new ApiError(
    response.status,
    problem.detail ?? problem.title ?? text ?? `${response.status} ${response.statusText}`.trim(),
    errors
  );
}

/** The WebSocket URL for live events and telemetry; browsers cannot send headers there, so the token is a query. */
export function eventsUrl(connection: Connection): string {
  const url = new URL('/ws', connection.baseUrl);
  url.protocol = url.protocol === 'https:' ? 'wss:' : 'ws:';
  if (connection.token) {
    url.searchParams.set('access_token', connection.token);
  }

  return url.toString();
}
