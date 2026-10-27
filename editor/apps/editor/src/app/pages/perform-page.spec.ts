import { type WritableSignal, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { GlobalSettings, PerformControl, Preset } from '@cathode/api-client';
import { CatalogStore } from '../state/catalog.store';
import { GlobalStore } from '../state/global.store';
import { LiveStore } from '../state/live.store';
import type { ResolvedControl } from './perform';
import { PerformDragType } from './perform-control';
import { PerformPage } from './perform-page';

const controls = (...blocks: string[]): PerformControl[] => blocks.map(block => ({ block, param: 'gain', label: null }));

describe('PerformPage', () => {
  function render() {
    const preset = signal({ blocks: [], perform: controls('a', 'b', 'c') } as unknown as Preset);
    const settings = signal({ perform: controls('x', 'y') } as unknown as GlobalSettings);
    const setPerform = vi.fn(() => Promise.resolve(true));
    TestBed.configureTestingModule({
      providers: [
        { provide: LiveStore, useValue: { preset, setPerform } },
        {
          provide: GlobalStore,
          useValue: {
            settings,
            update: (change: (s: GlobalSettings) => void) => {
              const next = structuredClone(settings());
              change(next);
              settings.set(next);
              return Promise.resolve(true);
            }
          }
        },
        { provide: CatalogStore, useValue: { info: () => undefined } }
      ]
    });
    TestBed.overrideComponent(PerformPage, { set: { template: '', imports: [] } });
    const fixture = TestBed.createComponent(PerformPage);
    fixture.detectChanges();
    const page = fixture.componentInstance as unknown as {
      page: WritableSignal<'preset' | 'global'>;
      controls: () => ResolvedControl[];
      dragging: WritableSignal<number | null>;
      dropTarget: () => number | null;
      rename: (c: ResolvedControl, label: string | null) => void;
      move: (from: number, to: number) => void;
      dragOver: (event: DragEvent, index: number) => void;
      drop: (event: DragEvent, index: number) => void;
    };
    const order = () => (setPerform.mock.lastCall as unknown as [PerformControl[]] | undefined)?.[0];
    return { page, setPerform, settings, order };
  }

  /** A drag event carrying a Perform control, as the grip's dragstart sets it up. */
  function dragEvent(type: string, types = [PerformDragType]): DragEvent {
    const event = new Event(type, { cancelable: true }) as DragEvent;
    Object.defineProperty(event, 'dataTransfer', { value: { types, dropEffect: 'none' } });
    return event;
  }

  it('saves a caption of its own with the preset', () => {
    const { page, order } = render();
    const [first] = page.controls();
    if (first) {
      page.rename(first, 'Crunch');
    }

    expect(order()?.map(c => c.label)).toEqual(['Crunch', null, null]);
  });

  it('moves a control by its buttons, not past the ends', () => {
    const { page, setPerform, order } = render();
    page.move(0, 1);
    expect(order()?.map(c => c.block)).toEqual(['b', 'a', 'c']);

    setPerform.mockClear();
    page.move(0, -1);
    page.move(2, 3);
    expect(setPerform).not.toHaveBeenCalled();
  });

  it('moves a dragged control onto the one it is dropped on', () => {
    const { page, order } = render();
    page.dragging.set(0);
    const over = dragEvent('dragover');
    page.dragOver(over, 2);
    expect(over.defaultPrevented).toBe(true);
    expect(page.dropTarget()).toBe(2);

    page.drop(dragEvent('drop'), 2);
    expect(order()?.map(c => c.block)).toEqual(['b', 'c', 'a']);
    expect(page.dragging()).toBeNull();
  });

  it('is no drop target for other drags', () => {
    const { page } = render();
    const over = dragEvent('dragover', ['Files']);
    page.dragOver(over, 1);
    expect(over.defaultPrevented).toBe(false);
  });

  it('edits the global page through the global settings', () => {
    const { page, setPerform, settings } = render();
    page.page.set('global');
    page.move(1, 0);
    const [first] = page.controls();
    if (first) {
      page.rename(first, 'Lead');
    }

    expect(setPerform).not.toHaveBeenCalled();
    expect(settings().perform).toEqual([
      { block: 'y', param: 'gain', label: 'Lead' },
      { block: 'x', param: 'gain', label: null }
    ]);
  });
});
