import { type Type, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { type GlobalSettings } from '@cathode/api-client';
import { Subject } from 'rxjs';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';
import { GlobalStore } from '../state/global.store';
import { ScopeDialog } from './scope-dialog';
import { TunerDialog } from './tuner-dialog';

// The tuner, scope and RTA are rig-wide: an editor only tells the Server what it changes, as itself.
describe('the analyser dialogs', () => {
  function render<T>(component: Type<T>) {
    const sent: { path: string; query: unknown; body: unknown }[] = [];
    const connected$ = new Subject<void>();
    const settings = signal({
      tunerOutput: 'mute',
      tunerView: 'needle',
      tuning: 'chromatic',
      tunerReference: 440,
      tunerOffsetsOn: false,
      tunerOffsets: [0, 0, 0, 0, 0, 0, 0, 0]
    } as GlobalSettings);
    const events = {
      clientId: 'me',
      connected$,
      scope: null,
      rta: null,
      latest: null as { tuner?: { frequency: number; note: string; octave: number; cents: number } } | null
    };
    TestBed.configureTestingModule({
      providers: [
        {
          provide: GlobalStore,
          useValue: {
            settings,
            update: (change: (g: GlobalSettings) => void) => {
              const next = structuredClone(settings());
              change(next);
              settings.set(next);
              return Promise.resolve(true);
            }
          }
        },
        {
          provide: ConnectionService,
          useValue: {
            client: {
              PUT: (path: string, init: { params?: { query?: unknown }; body: unknown }) => {
                sent.push({ path, query: init.params?.query, body: init.body });
                return Promise.resolve({});
              }
            }
          }
        },
        { provide: EventStream, useValue: events },
        { provide: Notify, useValue: { run: (action: () => Promise<unknown>) => action() } }
      ]
    });
    TestBed.overrideComponent(component, { set: { template: '', imports: [] } });
    const fixture = TestBed.createComponent(component);
    fixture.detectChanges();
    const open = (on: boolean) => {
      (fixture.componentInstance as unknown as { open: { set(value: boolean): void } }).open.set(on);
      fixture.detectChanges();
    };
    return { sent, open, connected$, fixture, settings, events };
  }

  it('the tuner stores the global Tuner Output, and only a change', async () => {
    const { fixture, settings, sent } = render(TunerDialog);
    const dialog = fixture.componentInstance as unknown as { setTunerOutput(value: string | null): void };

    dialog.setTunerOutput('bypass');
    await Promise.resolve();
    expect(settings().tunerOutput).toBe('bypass');

    dialog.setTunerOutput(null);
    dialog.setTunerOutput('bypass');
    await Promise.resolve();
    expect(settings().tunerOutput).toBe('bypass');
    expect(sent).toEqual([]);
  });

  it('the tuner stores its view, tuning, reference and offsets, clamped', async () => {
    const { fixture, settings } = render(TunerDialog);
    const dialog = fixture.componentInstance as unknown as {
      setView(value: string): void;
      setTuning(value: string): void;
      setReference(field: HTMLInputElement): void;
      setOffsetsOn(on: boolean): void;
      setOffset(string: number, field: HTMLInputElement): void;
    };
    const field = (value: string) => Object.assign(document.createElement('input'), { type: 'number', value });

    dialog.setView('strobe');
    dialog.setTuning('dropD');
    const reference = field('452.37');
    dialog.setReference(reference);
    dialog.setOffsetsOn(true);
    const offset = field('1.3');
    dialog.setOffset(2, offset);
    dialog.setOffset(6, field('-40'));
    await new Promise(resolve => setTimeout(resolve));

    expect(settings()).toMatchObject({
      tunerView: 'strobe',
      tuning: 'dropD',
      tunerReference: 450,
      tunerOffsetsOn: true,
      tunerOffsets: [0, 1.5, 0, 0, 0, -20, 0, 0]
    });
    // The fields show what was stored.
    expect(reference.value).toBe('450');
    expect(offset.value).toBe('1.5');
  });

  it('the tuner reads the detected pitch against the tuning, reference and offsets', () => {
    const { fixture, settings, events } = render(TunerDialog);
    const reading = (fixture.componentInstance as unknown as { reading: () => unknown }).reading;
    expect(reading()).toBeNull();

    // A D2 at pitch: chromatic says D2, Drop D says its string 6, and a +2 offset on string 6 reads it 2 cents flat.
    events.latest = { tuner: { frequency: 440 * 2 ** ((38 - 69) / 12), note: 'D', octave: 2, cents: 0 } };
    expect(reading()).toMatchObject({ note: 'D', octave: 2 });
    expect(reading()).not.toHaveProperty('string');
    settings.update(s => ({ ...s, tuning: 'dropD' }));
    expect(reading()).toMatchObject({ note: 'D', octave: 2, string: 6 });
    settings.update(s => ({ ...s, tunerOffsetsOn: true, tunerOffsets: [0, 0, 0, 0, 0, 2] }));
    expect((reading() as { cents: number }).cents).toBeCloseTo(-2, 6);
    // A tuning this editor does not know (saved by a newer one) reads as chromatic.
    settings.update(s => ({ ...s, tuning: 'nashville' }));
    expect(reading()).not.toHaveProperty('string');
  });

  it('the tuner sends nothing at startup, then its own on and off', async () => {
    const { sent, open, connected$ } = render(TunerDialog);
    connected$.next();
    await Promise.resolve();
    expect(sent).toEqual([]);

    open(true);
    open(false);
    await Promise.resolve();
    expect(sent).toEqual([
      { path: '/api/v1/tuner', query: { client: 'me' }, body: { bypassed: false } },
      { path: '/api/v1/tuner', query: { client: 'me' }, body: { bypassed: true } }
    ]);
  });

  it('the tuner turns itself on again after a reconnect while open', async () => {
    const { sent, open, connected$ } = render(TunerDialog);
    open(true);
    connected$.next();
    await Promise.resolve();
    expect(sent.map(s => s.body)).toEqual([{ bypassed: false }, { bypassed: false }]);
  });

  it('the analyser sends nothing at startup, then only what changes', async () => {
    const { sent, open } = render(ScopeDialog);
    await Promise.resolve();
    expect(sent).toEqual([]);

    open(true); // the scope view
    await Promise.resolve();
    expect(sent).toEqual([{ path: '/api/v1/scope', query: { client: 'me' }, body: { enabled: true } }]);

    open(false);
    await Promise.resolve();
    expect(sent.at(-1)).toEqual({ path: '/api/v1/scope', query: { client: 'me' }, body: { enabled: false } });
    expect(sent.filter(s => s.path === '/api/v1/rta')).toEqual([]);
  });
});
