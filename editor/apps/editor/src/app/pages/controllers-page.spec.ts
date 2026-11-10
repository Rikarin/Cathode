import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { type ControllerInfo, type Telemetry } from '@cathode/api-client';
import { ConnectionService } from '../core/connection';
import { Notify } from '../core/notify';
import { GlobalStore } from '../state/global.store';
import { LiveStore } from '../state/live.store';
import { ControllersPage, liveControllers, scopeName } from './controllers-page';

describe('scopeName', () => {
  it('names a modifier scope', () => {
    expect(scopeName(null)).toBe('All channels');
    expect(scopeName('all')).toBe('All channels');
    expect(scopeName('B')).toBe('Channel B only');
  });
});

describe('liveControllers', () => {
  const running: ControllerInfo = {
    running: true,
    sequencerStage: 2,
    sequencerStages: 8,
    lfo1: 0.5,
    lfo2: 0.25,
    adsr1: 0,
    adsr2: 1
  };
  const frame = (controllers?: ControllerInfo) => ({ type: 'telemetry', controllers }) as Telemetry;

  it('shows the controllers while they run and the telemetry is fresh', () => {
    expect(liveControllers(null, frame(running), 30)).toBe(running);
    expect(liveControllers(running, frame({ ...running, running: false }), 30)).toBeNull();
    expect(liveControllers(running, frame(running), 2_000)).toBeNull(); // the socket or the engine stopped
    expect(liveControllers(running, frame(), 30)).toBeNull(); // no shared-memory telemetry
    expect(liveControllers(null, null, 0)).toBeNull();
  });

  it('keeps the previous state until the stage changes or a value moves visibly', () => {
    expect(liveControllers(running, frame({ ...running, lfo1: 0.504 }), 30)).toBe(running);
    const stepped = { ...running, sequencerStage: 3 };
    expect(liveControllers(running, frame(stepped), 30)).toBe(stepped);
    const moved = { ...running, adsr1: 0.2 };
    expect(liveControllers(running, frame(moved), 30)).toBe(moved);
  });
});

describe('ControllersPage externals', () => {
  function render() {
    const put = vi.fn(() => Promise.resolve({ response: new Response(null, { status: 204 }) }));
    TestBed.configureTestingModule({
      providers: [
        { provide: LiveStore, useValue: { preset: signal(undefined), activeScene: signal(0) } },
        { provide: GlobalStore, useValue: { settings: signal(undefined) } },
        { provide: ConnectionService, useValue: { client: { PUT: put } } },
        { provide: Notify, useValue: { run: (action: () => Promise<unknown>) => action() } }
      ]
    });
    TestBed.overrideComponent(ControllersPage, { set: { template: '', imports: [] } });
    const fixture = TestBed.createComponent(ControllersPage);
    fixture.detectChanges();
    const page = fixture.componentInstance as unknown as {
      externals: () => readonly number[];
      setExternal: (index: number, value: number) => Promise<void>;
    };
    return { page, put };
  }

  it('offers External 1 to 16, at rest until set', () => {
    const { page } = render();
    expect(page.externals()).toEqual(Array.from({ length: 16 }, () => 0));
  });

  it('drives an External through PUT /live/external/{index} and keeps the value it sent', async () => {
    const { page, put } = render();
    await page.setExternal(0, 0.5);
    await page.setExternal(7, 1.4);

    expect(put).toHaveBeenCalledWith('/api/v1/live/external/{index}', {
      params: { path: { index: 0 } },
      body: { value: 0.5 }
    });
    expect(put).toHaveBeenCalledWith('/api/v1/live/external/{index}', {
      params: { path: { index: 7 } },
      body: { value: 1 }
    });
    expect(page.externals()).toEqual([0.5, 0, 0, 0, 0, 0, 0, 1, ...Array.from({ length: 8 }, () => 0)]);
  });
});

describe('ControllersPage controller settings', () => {
  function render() {
    const setControllers = vi.fn((_values: Record<string, number>, _gesture?: string) => Promise.resolve());
    TestBed.configureTestingModule({
      providers: [
        { provide: LiveStore, useValue: { preset: signal(undefined), activeScene: signal(0), setControllers } },
        { provide: GlobalStore, useValue: { settings: signal(undefined) } },
        { provide: ConnectionService, useValue: { client: {} } },
        { provide: Notify, useValue: { run: (action: () => Promise<unknown>) => action() } }
      ]
    });
    TestBed.overrideComponent(ControllersPage, { set: { template: '', imports: [] } });
    const fixture = TestBed.createComponent(ControllersPage);
    fixture.detectChanges();
    const page = fixture.componentInstance as unknown as {
      set: (id: string, value: number, control?: string) => Promise<void>;
      gesture: (control: string) => { end: () => void };
    };
    const gestures = () => setControllers.mock.calls.map(call => call[1]);
    return { page, setControllers, gestures };
  }

  it('sends one gesture id for a drag of a knob and another for the next drag', async () => {
    const { page, setControllers, gestures } = render();
    await page.set('lfo1Rate', 2);
    await page.set('lfo1Rate', 3);
    page.gesture('lfo1Rate').end();
    await page.set('lfo1Rate', 4);

    expect(setControllers.mock.calls.map(call => call[0])).toEqual([{ lfo1Rate: 2 }, { lfo1Rate: 3 }, { lfo1Rate: 4 }]);
    const [first, second, third] = gestures();
    expect(first).toEqual(expect.any(String));
    expect(second).toBe(first);
    expect(third).toEqual(expect.any(String));
    expect(third).not.toBe(first);
  });

  it('keeps the gestures of two knobs apart', async () => {
    const { page, gestures } = render();
    await page.set('lfo1Rate', 2);
    await page.set('lfo2Rate', 2);
    page.gesture('lfo1Rate').end();
    await page.set('lfo2Rate', 3);

    const [rate1, rate2, rate2Again] = gestures();
    expect(rate2).not.toBe(rate1);
    expect(rate2Again).toBe(rate2);
  });

  it('sends the stages one stroke of the step bars sets under one gesture', async () => {
    const { page, gestures } = render();
    await page.set('seq1', 0.5, 'seqBars');
    await page.set('seq2', 0.5, 'seqBars');
    page.gesture('seqBars').end();
    await page.set('seq2', 0.75, 'seqBars');

    const [first, second, next] = gestures();
    expect(second).toBe(first);
    expect(next).not.toBe(first);
  });
});
