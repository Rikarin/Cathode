import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ConnectionService } from '../core/connection';
import type { CathodeDesktop, SidecarStatus } from '../core/desktop';
import { SidecarStatuses } from '../core/engine-health';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';
import { GlobalStore } from '../state/global.store';
import { SetupPage } from './setup-page';

describe('SetupPage', () => {
  const engine = {
    connected: true,
    backend: 'CoreAudio',
    sampleRate: 48_000,
    bufferFrames: 64,
    cpuP999: 12,
    cpuMax: 14.5,
    overloads: 0,
    driverOverloads: 0,
    inputs: ['Input 1'],
    lastError: null
  };

  function setup(options: { desktop?: boolean } = {}) {
    const reads: string[] = [];
    const answers: Record<string, unknown> = {
      '/api/v1/system': { engine },
      '/api/v1/system/latency': { connected: false }
    };
    const client = {
      GET: (path: string) => {
        reads.push(path);
        return Promise.resolve({ data: answers[path], response: new Response('{}') });
      }
    };
    const desktop = {
      target: () => Promise.resolve({ mode: 'local' }),
      audio: {
        devices: () =>
          Promise.resolve({
            backend: 'CoreAudio',
            devices: [{ uid: 'scarlett', name: 'Scarlett', inputChannels: 2, outputChannels: 2, sampleRate: 48_000 }]
          }),
        settings: () =>
          Promise.resolve({
            device: 'scarlett',
            sampleRate: 48_000,
            bufferFrames: 64,
            loopIn: [],
            loopOut: [],
            outputs: [1],
            diOut: []
          })
      }
    } as unknown as CathodeDesktop;
    const sidecars = {
      statuses: signal([
        { name: 'host', state: 'running' },
        { name: 'server', state: 'running' }
      ] as SidecarStatus[]),
      logs: () => Promise.resolve({ host: ['audio: started', 'audio: overload'], server: [] })
    };
    const successes: string[] = [];
    const errors: unknown[] = [];
    TestBed.configureTestingModule({
      providers: [
        {
          provide: ConnectionService,
          useValue: { client, connection: signal(null), desktop: options.desktop === false ? null : desktop }
        },
        { provide: EventStream, useValue: { latest: null } },
        { provide: SidecarStatuses, useValue: sidecars },
        {
          provide: GlobalStore,
          useValue: {
            settings: signal({ inputs: [{ fullScaleVolts: 1.228 }] }),
            refresh: () => Promise.resolve(),
            update: () => Promise.resolve(true)
          }
        },
        {
          provide: Notify,
          useValue: {
            run: (action: () => Promise<unknown>) => action(),
            success: (message: string) => successes.push(message),
            error: (error: unknown) => errors.push(error)
          }
        }
      ]
    });
    const fixture = TestBed.createComponent(SetupPage);
    const page = fixture.nativeElement as HTMLElement;
    const settle = async () => {
      for (let i = 0; i < 3; i++) {
        fixture.detectChanges();
        await fixture.whenStable();
        await new Promise(resolve => setTimeout(resolve));
      }

      fixture.detectChanges();
    };
    const button = (text: string) =>
      [...page.querySelectorAll('button')].find(b => b.textContent.trim() === text) ?? null;
    const copyButton = () => page.querySelector<HTMLButtonElement>('[data-testid="copy-engine-log"]');
    return { fixture, page, reads, settle, button, copyButton, successes, errors };
  }

  afterEach(() => vi.unstubAllGlobals());

  it('shows the engine’s readings from the Server', async () => {
    const { page, settle } = setup();
    await settle();

    const readings = page.querySelector('[data-testid="engine-readings"]')?.textContent ?? '';
    expect(readings).toContain('CoreAudio');
    expect(readings).toContain('48.0');
    expect(readings).toContain('1.33'); // 64 frames at 48 kHz, in ms
  });

  it('shows each engine process’s last lines on request, and hides them again', async () => {
    const { page, settle, button, copyButton } = setup();
    await settle();
    expect(page.querySelector('[role="log"]')).toBeNull();
    expect(copyButton()).toBeNull();

    button('Show engine log')?.click();
    await settle();

    expect(page.querySelector('[role="log"]')?.textContent).toBe(
      'cathode-host\naudio: started\naudio: overload\n\ncathode-server\n(nothing yet)'
    );

    button('Hide engine log')?.click();
    await settle();
    expect(page.querySelector('[role="log"]')).toBeNull();
    expect(copyButton()).toBeNull();
  });

  it('copies the engine log as shown (#254)', async () => {
    const copied: string[] = [];
    vi.stubGlobal('navigator', {
      clipboard: {
        writeText: (text: string) => {
          copied.push(text);
          return Promise.resolve();
        }
      }
    });
    const { page, settle, button, copyButton, successes } = setup();
    await settle();
    button('Show engine log')?.click();
    await settle();

    copyButton()?.click();
    await settle();

    expect(copied).toEqual(['cathode-host\naudio: started\naudio: overload\n\ncathode-server\n(nothing yet)']);
    expect(copied[0]).toBe(page.querySelector('[role="log"]')?.textContent);
    expect(successes).toEqual(['Copied the engine log.']);
  });

  it('says so when the clipboard refuses the log', async () => {
    const refusal = new Error('Write permission denied.');
    vi.stubGlobal('navigator', { clipboard: { writeText: () => Promise.reject(refusal) } });
    const { settle, button, copyButton, successes, errors } = setup();
    await settle();
    button('Show engine log')?.click();
    await settle();

    copyButton()?.click();
    await settle();

    expect(errors).toEqual([refusal]);
    expect(successes).toEqual([]);
  });

  it('offers no engine log in a browser, which supervises no engine process', async () => {
    const { page, settle, button } = setup({ desktop: false });
    await settle();

    expect(button('Show engine log')).toBeNull();
    expect(page.querySelector('[data-testid="engine-processes"]')).toBeNull();
  });

  it('reads the engine again every two seconds, and no more once it is destroyed', async () => {
    vi.useFakeTimers();
    try {
      const { fixture, reads } = setup({ desktop: false });
      fixture.detectChanges();
      await vi.advanceTimersByTimeAsync(0);
      expect(reads).toEqual(['/api/v1/system', '/api/v1/system/latency']);

      await vi.advanceTimersByTimeAsync(2_000);
      expect(reads).toHaveLength(4);

      fixture.destroy();
      await vi.advanceTimersByTimeAsync(10_000);
      expect(reads).toHaveLength(4);
    } finally {
      vi.useRealTimers();
    }
  });
});
