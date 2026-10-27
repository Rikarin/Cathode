import type { SidecarStatus } from './desktop';
import { engineHealth } from './engine-health';

const host = (state: SidecarStatus['state'], detail: string | null = null): SidecarStatus => ({
  name: 'host',
  state,
  detail
});

describe('the connection light', () => {
  it('follows the WebSocket while it is not open', () => {
    expect(engineHealth('closed', host('running'), false)).toEqual({
      presence: 'dnd',
      text: 'Disconnected: retrying'
    });
    expect(engineHealth('connecting', host('running'), false).presence).toBe('idle');
  });

  it('is green with the socket open, the host running and telemetry coming', () => {
    expect(engineHealth('open', host('running'), false)).toEqual({
      presence: 'online',
      text: 'Connected to the engine'
    });
    // A browser or a remote Server: no host to ask about.
    expect(engineHealth('open', undefined, false).presence).toBe('online');
  });

  it('says so while the desktop app restarts a crashed host, though the Server still answers', () => {
    expect(engineHealth('open', host('restarting', 'exited (1); restarting in 2 s'), false)).toEqual({
      presence: 'idle',
      text: 'The engine stopped: exited (1); restarting in 2 s'
    });
    expect(engineHealth('open', host('failed', 'cathode-host not found'), false)).toEqual({
      presence: 'dnd',
      text: 'The engine failed to start: cathode-host not found'
    });
    expect(engineHealth('open', host('starting'), false).presence).toBe('idle');
  });

  it('turns amber when telemetry stops, whatever the reason', () => {
    expect(engineHealth('open', host('running'), true).presence).toBe('idle');
    expect(engineHealth('open', undefined, true).text).toMatch(/no meters/);
  });
});
