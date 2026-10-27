import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { EventStream } from '../core/events';
import { ShortcutService } from '../shell/shortcuts';
import { BlockPresetsStore } from '../state/block-presets.store';
import { CatalogStore } from '../state/catalog.store';
import { GlobalStore } from '../state/global.store';
import { LiveStore } from '../state/live.store';
import { UiStore } from '../state/ui.store';
import { GridEditor } from './grid-editor';

describe('GridEditor shortcuts', () => {
  beforeAll(() => {
    globalThis.ResizeObserver ??= class {
      observe(): void {}
      disconnect(): void {}
    } as unknown as typeof ResizeObserver;
  });

  function render() {
    const removed: string[] = [];
    TestBed.configureTestingModule({
      providers: [
        {
          provide: LiveStore,
          useValue: {
            // The grid's focus starts on row 1, column 2: the drive.
            preset: signal({
              rows: 6,
              columns: 14,
              blocks: [{ id: 'drive1', type: 'drive', row: 0, column: 1, channels: {}, assets: {} }],
              cables: []
            }),
            selectedId: signal(null),
            removeBlock: async (id: string) => removed.push(id)
          }
        },
        { provide: EventStream, useValue: {} },
        { provide: CatalogStore, useValue: { info: () => undefined } },
        { provide: UiStore, useValue: {} },
        { provide: BlockPresetsStore, useValue: {} },
        { provide: GlobalStore, useValue: { settings: signal(null) } }
      ]
    });
    TestBed.overrideComponent(GridEditor, {
      set: { template: '<div #surface tabindex="0" role="grid"></div>', imports: [] }
    });
    const fixture = TestBed.createComponent(GridEditor);
    fixture.detectChanges();
    document.body.append(fixture.nativeElement);
    const surface = fixture.nativeElement.querySelector('[role=grid]') as HTMLElement;
    const shortcuts = TestBed.inject(ShortcutService);
    const press = (target: HTMLElement, key: string) => {
      const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
      target.dispatchEvent(event);
      shortcuts.dispatch(event);
    };
    return { fixture, surface, press, removed };
  }

  afterEach(() => document.body.replaceChildren());

  it('removes the focused block with Delete or Backspace while the grid has the focus', () => {
    const { surface, press, removed } = render();
    surface.focus();
    press(surface, 'Delete');
    press(surface, 'Backspace');
    expect(removed).toEqual(['drive1', 'drive1']);
  });

  it('leaves the block alone when Delete is pressed elsewhere on the page', () => {
    const { press, removed } = render();
    const page = Object.assign(document.createElement('div'), { tabIndex: -1 });
    document.body.append(page);
    page.focus();
    press(page, 'Delete');
    press(document.body, 'Backspace');
    expect(removed).toEqual([]);
  });
});
