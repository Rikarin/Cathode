import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { LooperInfo } from '@cathode/api-client';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';
import { GlobalStore } from '../state/global.store';
import { LibraryStore } from '../state/library.store';
import { LiveStore } from '../state/live.store';
import { Transport } from './transport';

// The tempo field and the looper: what the toolbar shows comes from telemetry, frame by frame.
describe('Transport', () => {
  function render() {
    // The component reads telemetry on animation frames: the test runs them by hand.
    let frame: FrameRequestCallback | null = null;
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
      frame = callback;
      return 1;
    });
    vi.stubGlobal('cancelAnimationFrame', () => undefined);

    const events = { latest: null as { tempo: number; looper?: Partial<LooperInfo> | null } | null };
    const setTempo = vi.fn(() => Promise.resolve());
    const post = vi.fn(() => Promise.resolve({}));
    const download = vi.fn(() => Promise.resolve());
    const failures: unknown[] = [];
    TestBed.configureTestingModule({
      providers: [
        { provide: LiveStore, useValue: { setTempo, tap: vi.fn() } },
        { provide: GlobalStore, useValue: { settings: signal(null), update: vi.fn() } },
        { provide: EventStream, useValue: events },
        { provide: ConnectionService, useValue: { client: { POST: post } } },
        { provide: LibraryStore, useValue: { download } },
        {
          provide: Notify,
          useValue: {
            run: async (action: () => Promise<unknown>) => {
              try {
                return await action();
              } catch (error) {
                failures.push(error);
                return undefined;
              }
            }
          }
        }
      ]
    });
    TestBed.overrideComponent(Transport, { set: { template: '', imports: [] } });
    const fixture = TestBed.createComponent(Transport);
    fixture.detectChanges();
    const transport = fixture.componentInstance as unknown as {
      editing: { (): boolean; set(on: boolean): void };
      tempo(): number;
      looper(): LooperInfo | null;
      state(): string;
      canExport(): boolean;
      recordColor(): string | null;
      stateColor(): string;
      looperSource(): { name: string };
      startEditing(): void;
      setTempo(text: string): void;
      act(action: string): Promise<void>;
      export(): void;
    };
    /** One animation frame with this telemetry. */
    const tick = (latest: typeof events.latest) => {
      events.latest = latest;
      frame?.(0);
    };
    return { transport, tick, setTempo, post, download, failures, fixture };
  }

  afterEach(() => vi.unstubAllGlobals());

  it('shows the engine’s tempo, to a tenth of a BPM', () => {
    const { transport, tick } = render();
    expect(transport.tempo()).toBe(120);

    tick({ tempo: 97.4449 });
    expect(transport.tempo()).toBe(97.4);
  });

  it('keeps the last tempo but drops the looper when telemetry stops', () => {
    const { transport, tick } = render();
    tick({ tempo: 140, looper: { state: 'playing', positionSeconds: 1, lengthSeconds: 4, source: 'rig' } });
    expect(transport.state()).toBe('playing');

    tick(null);
    expect(transport.tempo()).toBe(140);
    expect(transport.looper()).toBeNull();
    expect(transport.state()).toBe('empty');
  });

  it('follows the looper’s state, and its position only in tenths of a second', () => {
    const { transport, tick } = render();
    const looper = { state: 'playing', positionSeconds: 1, lengthSeconds: 4, source: 'rig' } as const;
    tick({ tempo: 120, looper });
    const shown = transport.looper();

    // A few milliseconds on: the same object, so nothing is drawn again.
    tick({ tempo: 120, looper: { ...looper, positionSeconds: 1.04 } });
    expect(transport.looper()).toBe(shown);

    tick({ tempo: 120, looper: { ...looper, positionSeconds: 1.2 } });
    expect(transport.looper()?.positionSeconds).toBe(1.2);

    tick({ tempo: 120, looper: { ...looper, positionSeconds: 1.2, reversed: true } });
    expect(transport.looper()?.reversed).toBe(true);
  });

  it('colours Record and the state tag by what the looper is doing', () => {
    const { transport, tick } = render();
    expect(transport.recordColor()).toBeNull();
    expect(transport.stateColor()).toBe('none');

    tick({ tempo: 120, looper: { state: 'armed' } });
    expect(transport.recordColor()).toBe('warning');
    expect(transport.stateColor()).toBe('warning');

    tick({ tempo: 120, looper: { state: 'recording' } });
    expect(transport.recordColor()).toBe('error');
    expect(transport.stateColor()).toBe('error');

    tick({ tempo: 120, looper: { state: 'playing' } });
    expect(transport.recordColor()).toBeNull();
    expect(transport.stateColor()).toBe('success');
  });

  it('names the looper the buttons drive: the preset’s block, or the rig’s', () => {
    const { transport, tick } = render();
    expect(transport.looperSource().name).toBe('Rig looper');

    tick({ tempo: 120, looper: { state: 'stopped', source: 'block' } });
    expect(transport.looperSource().name).toBe('Looper block');
  });

  it('exports only a loop that plays or is stopped', () => {
    const { transport, tick, download } = render();
    expect(transport.canExport()).toBe(false);

    tick({ tempo: 120, looper: { state: 'recording', lengthSeconds: 2 } });
    expect(transport.canExport()).toBe(false);
    tick({ tempo: 120, looper: { state: 'stopped', lengthSeconds: 0 } });
    expect(transport.canExport()).toBe(false);
    tick({ tempo: 120, looper: { state: 'stopped', lengthSeconds: 2 } });
    expect(transport.canExport()).toBe(true);
    tick({ tempo: 120, looper: { state: 'playing', lengthSeconds: 2 } });
    expect(transport.canExport()).toBe(true);

    transport.export();
    expect(download).toHaveBeenCalledWith('/api/v1/looper/export', 'loop.wav');
  });

  it('sets a typed tempo between 30 and 300 BPM, and closes the field either way', () => {
    const { transport, setTempo } = render();

    transport.startEditing();
    transport.setTempo('96.5');
    expect(setTempo).toHaveBeenCalledWith(96.5);
    expect(transport.editing()).toBe(false);

    for (const refused of ['29', '301', 'fast', '']) {
      transport.startEditing();
      transport.setTempo(refused);
      expect(transport.editing()).toBe(false);
    }

    // '' is 0 as a number, which is out of range like the others.
    expect(setTempo).toHaveBeenCalledTimes(1);
  });

  it('does not take the blur of a field Escape closed as a commit', () => {
    const { transport, setTempo } = render();
    transport.startEditing();

    transport.editing.set(false); // Escape
    transport.setTempo('200'); // the blur the field's removal fires

    expect(setTempo).not.toHaveBeenCalled();
  });

  it('posts a looper action, and reports a refusal instead of throwing', async () => {
    const { transport, post, failures } = render();

    await transport.act('overdub');
    expect(post).toHaveBeenCalledWith('/api/v1/looper/{action}', { params: { path: { action: 'overdub' } } });

    post.mockRejectedValueOnce(new Error('No loop to overdub'));
    await transport.act('overdub');
    expect(failures).toHaveLength(1);
  });
});
