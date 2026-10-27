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
    const settings = signal({ tunerOutput: 'mute' } as GlobalSettings);
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
        { provide: EventStream, useValue: { clientId: 'me', connected$, scope: null, rta: null } },
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
    return { sent, open, connected$, fixture, settings };
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
