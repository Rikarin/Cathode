import { Component, DestroyRef, inject, input, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { type GlobalSettings } from '@cathode/api-client';
import { CthTuner } from '@cathode/audio-ui';
import { Subject } from 'rxjs';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';
import { GlobalStore } from '../state/global.store';
import { TunerDialog } from './tuner-dialog';

// What the tuner sends and stores is in analyser-dialogs.spec.ts; this renders the dialog itself.
describe('TunerDialog', () => {
  let displays = 0;

  /** Stands in for the tuner display, which draws on every animation frame while it lives. */
  @Component({ selector: 'cth-tuner', template: '' })
  class TunerStub {
    readonly source = input<unknown>();
    readonly view = input<string>();

    constructor() {
      displays++;
      inject(DestroyRef).onDestroy(() => displays--);
    }
  }

  function render(tuning: string) {
    displays = 0;
    const settings = signal({
      tunerOutput: 'mute',
      tunerView: 'needle',
      tuning,
      tunerReference: 440,
      tunerOffsetsOn: true,
      tunerOffsets: [0, 0, 0, 0, 0, 0, 0, 0]
    } as GlobalSettings);
    TestBed.configureTestingModule({
      providers: [
        { provide: GlobalStore, useValue: { settings, update: () => Promise.resolve(true) } },
        { provide: ConnectionService, useValue: { client: { PUT: () => Promise.resolve({}) } } },
        { provide: EventStream, useValue: { clientId: 'me', connected$: new Subject<void>(), latest: null } },
        { provide: Notify, useValue: { run: (action: () => Promise<unknown>) => action() } }
      ]
    });
    TestBed.overrideComponent(TunerDialog, { remove: { imports: [CthTuner] }, add: { imports: [TunerStub] } });
    const fixture = TestBed.createComponent(TunerDialog);
    fixture.detectChanges();
    const open = (on: boolean) => {
      fixture.componentRef.setInput('open', on);
      fixture.detectChanges();
    };
    return { open, fixture, settings };
  }

  it('draws the tuner only while the dialog is open', () => {
    const { open, fixture } = render('chromatic');
    expect(displays).toBe(0);

    open(true);
    expect(displays).toBe(1);

    open(false);
    expect(displays).toBe(0);
    fixture.destroy();
  });

  it('offers an offset per string of the tuning, and none for chromatic', () => {
    const { open, fixture, settings } = render('dropD');
    open(true);
    // The dialog's content is in an overlay, outside the fixture's element.
    const offsets = () => document.querySelectorAll('[data-testid=tuner-string-offsets] input');
    expect(offsets()).toHaveLength(6);
    expect(offsets()[5]?.getAttribute('aria-label')).toBe('String 6 offset in cents');

    settings.update(s => ({ ...s, tuning: 'chromatic' }));
    fixture.detectChanges();
    expect(offsets()).toHaveLength(0);
    expect(document.querySelector('[data-testid=tuner-offsets]')).toBeNull();
    fixture.destroy();
  });
});
