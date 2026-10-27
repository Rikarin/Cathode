import { type Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';
import { ScopeDialog } from './scope-dialog';
import { TunerDialog } from './tuner-dialog';

// The tuner, scope and RTA are rig-wide: an editor only tells the Server what it changes, as itself.
describe('the analyser dialogs', () => {
  function render<T>(component: Type<T>) {
    const sent: { path: string; query: unknown; body: unknown }[] = [];
    const connected$ = new Subject<void>();
    TestBed.configureTestingModule({
      providers: [
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
    return { sent, open, connected$, fixture };
  }

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
