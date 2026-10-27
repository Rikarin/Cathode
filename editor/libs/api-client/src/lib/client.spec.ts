import { ApiError, createCathodeClient, eventsUrl, unwrap } from './client';

describe('api-client', () => {
  it('sends the bearer token on every request', async () => {
    const seen: Request[] = [];
    const client = createCathodeClient({ baseUrl: 'http://127.0.0.1:5180', token: 't0ken' }, async request => {
      seen.push(request as Request);
      return new Response(JSON.stringify({ version: '1', engine: {} }), {
        headers: { 'Content-Type': 'application/json' }
      });
    });

    await client.GET('/api/v1/system');

    expect(seen[0].url).toBe('http://127.0.0.1:5180/api/v1/system');
    expect(seen[0].headers.get('Authorization')).toBe('Bearer t0ken');
  });

  it('turns ProblemDetails into an ApiError with the reason', () => {
    const response = new Response(null, { status: 422 });
    expect(() =>
      unwrap({ response, error: { detail: 'The engine could not load the result.', errors: ['no cable'] } })
    ).toThrowError(ApiError);
    try {
      unwrap({ response, error: { detail: 'Nope.', errors: ['x'] } });
    } catch (error) {
      expect((error as ApiError).status).toBe(422);
      expect((error as ApiError).errors).toEqual(['x']);
    }
  });

  it('rejects a call the Server refuses, so a caller that does not unwrap still sees the failure', async () => {
    const client = createCathodeClient({ baseUrl: 'http://127.0.0.1:5180', token: null }, async () =>
      Response.json({ title: 'Conflict', detail: 'The engine is not connected.', errors: ['ipc'] }, { status: 503 })
    );

    const error = await client.POST('/api/v1/live/tap').catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(503);
    expect((error as ApiError).message).toBe('The engine is not connected.');
    expect((error as ApiError).errors).toEqual(['ipc']);
  });

  it('names the status when a refusal has no ProblemDetails, and the text when it is a plain string', async () => {
    const empty = createCathodeClient(
      { baseUrl: 'http://127.0.0.1:5180', token: null },
      async () => new Response(null, { status: 401, statusText: 'Unauthorized' })
    );
    const text = createCathodeClient({ baseUrl: 'http://127.0.0.1:5180', token: null }, async () =>
      Response.json('Unknown looper action.', { status: 400 })
    );

    await expect(empty.GET('/api/v1/live')).rejects.toThrowError('401 Unauthorized');
    await expect(text.POST('/api/v1/looper/{action}', { params: { path: { action: 'x' } } })).rejects.toThrowError(
      'Unknown looper action.'
    );
  });

  it('resolves a successful call as before', async () => {
    const client = createCathodeClient(
      { baseUrl: 'http://127.0.0.1:5180', token: null },
      async () => new Response(null, { status: 204 })
    );

    const result = await client.POST('/api/v1/live/tap');

    expect(result.response.status).toBe(204);
  });

  it('builds the WebSocket URL with the token as a query parameter', () => {
    expect(eventsUrl({ baseUrl: 'https://rig.local:5180', token: 'a b' })).toBe(
      'wss://rig.local:5180/ws?access_token=a+b'
    );
    expect(eventsUrl({ baseUrl: 'http://127.0.0.1:5180', token: null })).toBe('ws://127.0.0.1:5180/ws');
  });
});
