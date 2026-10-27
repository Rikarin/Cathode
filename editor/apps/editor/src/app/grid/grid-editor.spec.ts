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
    // jsdom has no ResizeObserver; the grid only needs one to exist.
    if (!('ResizeObserver' in globalThis)) {
      globalThis.ResizeObserver = class {
        observe = () => undefined;
        disconnect = () => undefined;
      } as unknown as typeof ResizeObserver;
    }
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
            removeBlock: (id: string) => Promise.resolve(removed.push(id))
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
    document.body.append(fixture.nativeElement as HTMLElement);
    const surface = (fixture.nativeElement as HTMLElement).querySelector('[role=grid]') as HTMLElement;
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

describe('GridEditor accessibility', () => {
  beforeAll(() => {
    // jsdom has no ResizeObserver; the grid only needs one to exist.
    if (!('ResizeObserver' in globalThis)) {
      globalThis.ResizeObserver = class {
        observe = () => undefined;
        disconnect = () => undefined;
      } as unknown as typeof ResizeObserver;
    }
  });

  afterEach(() => document.body.replaceChildren());

  const block = (id: string, type: string, row: number, column: number) => ({
    id,
    type,
    row,
    column,
    channels: {},
    assets: {},
    bypassMode: 'thru',
    sceneIgnore: []
  });

  function render() {
    const added: string[] = [];
    const removed: string[] = [];
    const preset = signal({
      rows: 2,
      columns: 4,
      blocks: [block('drive1', 'drive', 0, 0), block('delay1', 'delay', 0, 1), block('reverb1', 'reverb', 1, 1)],
      cables: [{ from: 'drive1', to: 'delay1' }]
    });
    TestBed.configureTestingModule({
      providers: [
        {
          provide: LiveStore,
          useValue: {
            preset,
            selectedId: signal(null),
            isBypassed: () => false,
            channelOf: () => 'A',
            valueOf: () => undefined,
            addCable: (from: string, to: string) => Promise.resolve(added.push(`${from}>${to}`)),
            removeCable: (from: string, to: string) => Promise.resolve(removed.push(`${from}>${to}`)),
            removeBlock: (id: string) => Promise.resolve(removed.push(id))
          }
        },
        { provide: EventStream, useValue: { blockMeter: () => 0 } },
        { provide: CatalogStore, useValue: { info: () => undefined, placeable: () => [] } },
        { provide: UiStore, useValue: { blockInfo: () => false } },
        { provide: BlockPresetsStore, useValue: {} },
        { provide: GlobalStore, useValue: { settings: signal(null) } }
      ]
    });
    const fixture = TestBed.createComponent(GridEditor);
    document.body.append(fixture.nativeElement as HTMLElement);
    fixture.detectChanges();
    const grid = (fixture.nativeElement as HTMLElement).querySelector('[role=grid]') as HTMLElement;
    const shortcuts = TestBed.inject(ShortcutService);
    const press = (key: string, options: KeyboardEventInit = {}) => {
      const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...options });
      grid.dispatchEvent(event);
      shortcuts.dispatch(event);
      fixture.detectChanges();
    };
    const status = () =>
      (fixture.nativeElement as HTMLElement).querySelector('[data-testid=grid-status]')?.textContent.trim();
    grid.focus();
    return { fixture, grid, press, status, added, removed };
  }

  it('puts every cell in a row, as the ARIA grid pattern requires', () => {
    const { grid } = render();
    const rows = grid.querySelectorAll(':scope > [role=row]');
    expect(rows).toHaveLength(2);
    const cells = [...grid.querySelectorAll('[role=gridcell]')];
    expect(cells).toHaveLength(8);
    expect(cells.every(cell => cell.parentElement?.getAttribute('role') === 'row')).toBe(true);
    expect(rows[1]?.getAttribute('aria-rowindex')).toBe('2');
    expect(cells[5]?.getAttribute('aria-colindex')).toBe('2');
  });

  it('cables two blocks from the keyboard: K, the arrows, Enter', () => {
    const { press, status, added } = render();
    // The focus starts on row 1, column 2 (the delay): go to the drive, then cable it to the reverb.
    press('ArrowLeft');
    press('k');
    expect(status()).toMatch(/^Cabling from drive drive1/);
    press('ArrowDown');
    press('ArrowRight');
    press('Enter');
    expect(added).toEqual(['drive1>reverb1']);
    expect(status()).toBe('');
  });

  it('cancels a keyboard cable with Escape', () => {
    const { press, status, added } = render();
    press('ArrowLeft');
    press('k');
    press('Escape');
    press('ArrowDown');
    press('ArrowRight');
    press('Enter'); // opens the palette rather than landing a cable
    expect(added).toEqual([]);
    expect(status()).toBe('');
  });

  it('selects a single cable from the keyboard and removes it with Delete', () => {
    const { press, status, removed } = render();
    press('K', { shiftKey: true });
    expect(status()).toBe('Cable from drive drive1 to delay delay1 selected: Delete removes it.');
    press('Delete');
    expect(removed).toEqual(['drive1>delay1']);
  });
});
