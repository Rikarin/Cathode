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

/** A typed client for REST API v1, generated from the Server's committed OpenAPI document. */
export function createCathodeClient(connection: Connection, fetchImpl: typeof fetch = globalThis.fetch) {
    const client = createClient<paths>({
        baseUrl: connection.baseUrl,
        fetch: (request: Request) => fetchImpl(request)
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

    const problem = (result.error ?? {}) as { detail?: string; title?: string; errors?: unknown };
    const errors = Array.isArray(problem.errors) ? problem.errors.map(String) : [];
    throw new ApiError(
        result.response.status,
        problem.detail ?? problem.title ?? `${result.response.status} ${result.response.statusText}`,
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
