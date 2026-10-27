import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { SwitchAction } from '@cathode/api-client';
import { CatalogStore } from '../state/catalog.store';
import { GlobalStore } from '../state/global.store';
import { LibraryStore } from '../state/library.store';
import { LiveStore } from '../state/live.store';
import { ActionPicker } from './action-picker';

describe('ActionPicker', () => {
  function render(action: SwitchAction | null) {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: LiveStore,
          useValue: {
            preset: signal({
              blocks: [
                { id: 'in1', type: 'input' },
                { id: 'delay1', type: 'delay' }
              ]
            })
          }
        },
        {
          provide: CatalogStore,
          useValue: { info: (type: string) => (type === 'delay' ? { name: 'Delay' } : undefined) }
        },
        {
          provide: LibraryStore,
          useValue: { presets: signal([]), banks: signal([{ id: '3f9a0c', name: 'Friday gig', slots: [] }]) }
        },
        { provide: GlobalStore, useValue: { settings: signal({ footswitches: [], activeLayout: null }) } }
      ]
    });
    const fixture = TestBed.createComponent(ActionPicker);
    fixture.componentRef.setInput('label', 'Switch 1 tap');
    fixture.componentRef.setInput('action', action);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const shown = (part: string) => host.querySelector(`[aria-label="Switch 1 tap ${part}"]`)?.textContent.trim();
    return { shown };
  }

  it('shows a stored block id and bank id by name', () => {
    expect(render({ action: 'bypass', target: 'delay1', value: null }).shown('target')).toContain('Delay (delay1)');
    TestBed.resetTestingModule();
    const slot = render({ action: 'presetSlot', target: '3f9a0c', value: 4 });
    expect(slot.shown('target')).toContain('Friday gig');
    expect(slot.shown('value')).toContain('Slot 5');
  });

  it('shows only the action list for an action without a target or value', () => {
    const { shown } = render({ action: 'tuner', target: null, value: null });
    expect(shown('action')).toContain('tuner');
    expect(shown('target')).toBeUndefined();
    expect(shown('value')).toBeUndefined();
  });
});
