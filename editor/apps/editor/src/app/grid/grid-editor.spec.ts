import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { EventStream } from '../core/events';
import { ShortcutService } from '../shell/shortcuts';
import { BlockPresetsStore } from '../state/block-presets.store';
import { CatalogStore } from '../state/catalog.store';
import { GlobalStore } from '../state/global.store';
import { LiveStore } from '../state/live.store';
import { UiStore } from '../state/ui.store';
import { inlet } from './geometry';
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

describe('GridEditor drop, paste and wire', () => {
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

  const BlockMime = 'application/x-cathode-block';

  interface Block {
    id: string;
    type: string;
    row: number;
    column: number;
    channels: Record<string, Record<string, number>>;
    assets: Record<string, string>;
    bypassMode: string;
    sceneIgnore: string[];
  }

  const block = (id: string, type: string, row: number, column: number, more: Partial<Block> = {}): Block => ({
    id,
    type,
    row,
    column,
    channels: {},
    assets: {},
    bypassMode: 'thru',
    sceneIgnore: [],
    ...more
  });

  /**
   * A 2 x 4 grid; the focus starts on row 1, column 2 (the delay).
   *
   *   in1   delay1  shunt1  .
   *   cab1  drive1  drive2  .
   */
  function render() {
    const calls: string[] = [];
    const preset = signal({
      rows: 2,
      columns: 4,
      blocks: [
        block('in1', 'input', 0, 0),
        block('delay1', 'delay', 0, 1),
        block('shunt1', 'shunt', 0, 2),
        block('cab1', 'cab', 1, 0, { channels: { A: { mic: 2 }, B: {} }, assets: { ir: 'ir-42' } }),
        block('drive1', 'drive', 1, 1, { channels: { A: { gain: 7, tone: 4 }, B: { gain: 3 } } }),
        block('drive2', 'drive', 1, 2)
      ],
      cables: [{ from: 'in1', to: 'delay1' }]
    });
    const counts = new Map<string, number>();
    const record = (call: string) => {
      calls.push(call);
      return Promise.resolve(true);
    };
    const live = {
      preset,
      selectedId: signal<string | null>(null),
      isBypassed: () => false,
      channelOf: () => 'A',
      valueOf: () => undefined,
      /** Places a block like the Server: the answer holds it, numbered after its type, replacing a shunt there. */
      addBlock: (type: string, row: number, column: number, settings?: { channels: unknown; assets: unknown }) => {
        const n = (counts.get(type) ?? 8) + 1;
        counts.set(type, n);
        preset.update(p => ({
          ...p,
          blocks: [
            ...p.blocks.filter(b => b.row !== row || b.column !== column),
            block(`${type}${n}`, type, row, column)
          ]
        }));
        const placed = settings ? ` ${JSON.stringify(settings.channels)} ${JSON.stringify(settings.assets)}` : '';
        return record(`add ${type} ${row}:${column}${placed}`);
      },
      moveBlock: (id: string, row: number, column: number) => record(`move ${id} ${row}:${column}`),
      dropBlock: (id: string, target: string, mode: string) => record(`${mode} ${id} ${target}`),
      removeBlock: (id: string) => record(`remove ${id}`),
      addCable: (from: string, to: string) => record(`cable ${from}>${to}`),
      removeCable: (from: string, to: string) => record(`uncable ${from}>${to}`),
      pasteSettings: (id: string, type: string, channels: unknown, assets: unknown) =>
        record(`settings ${id} ${type} ${JSON.stringify(channels)} ${JSON.stringify(assets)}`)
    };
    const imported: { id: string; type: string }[] = [];
    const blockPresets = {
      import: (file: File) => {
        calls.push(`import ${file.name}`);
        return Promise.resolve(imported);
      },
      apply: (id: string, entry: string, options: unknown) => record(`apply ${entry} ${id} ${JSON.stringify(options)}`)
    };
    TestBed.configureTestingModule({
      providers: [
        { provide: LiveStore, useValue: live },
        { provide: EventStream, useValue: { blockMeter: () => 0 } },
        { provide: CatalogStore, useValue: { info: () => undefined, placeable: () => [] } },
        { provide: UiStore, useValue: { blockInfo: () => false } },
        { provide: BlockPresetsStore, useValue: blockPresets },
        { provide: GlobalStore, useValue: { settings: signal(null) } }
      ]
    });
    const fixture = TestBed.createComponent(GridEditor);
    document.body.append(fixture.nativeElement as HTMLElement);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const grid = host.querySelector('[role=grid]') as HTMLElement;
    grid.setPointerCapture = () => undefined; // jsdom has no pointer capture
    const cell = (row: number, column: number) =>
      host.querySelector(`[data-testid=cell-${row}-${column}]`) as HTMLElement;
    const shortcuts = TestBed.inject(ShortcutService);
    const press = (key: string, options: KeyboardEventInit = {}) => {
      const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...options });
      grid.dispatchEvent(event);
      shortcuts.dispatch(event);
      fixture.detectChanges();
    };
    /** Lets the component's awaited store calls run to the end. */
    const settle = async () => {
      await new Promise(resolve => setTimeout(resolve));
      fixture.detectChanges();
    };
    grid.focus();
    return { fixture, host, grid, cell, press, settle, calls, live, imported };
  }

  /** A drag event as the browser makes it: jsdom has neither DragEvent nor DataTransfer. */
  function drag(
    type: 'dragstart' | 'dragover' | 'drop',
    transfer: { types?: string[]; data?: Record<string, string>; files?: File[] },
    keys: { altKey?: boolean; metaKey?: boolean; ctrlKey?: boolean } = {}
  ) {
    const data: Record<string, string> = { ...transfer.data };
    const dataTransfer = {
      types: transfer.types ?? Object.keys(data),
      files: transfer.files ?? [],
      dropEffect: 'none',
      effectAllowed: 'uninitialized',
      getData: (format: string) => data[format] ?? '',
      setData: (format: string, value: string) => (data[format] = value)
    };
    const event = Object.assign(new Event(type, { bubbles: true, cancelable: true }), {
      altKey: false,
      metaKey: false,
      ctrlKey: false,
      ...keys
    });
    Object.defineProperty(event, 'dataTransfer', { value: dataTransfer });
    return { event, dataTransfer, data };
  }

  function dropBlock(target: HTMLElement, id: string, keys: Parameters<typeof drag>[2] = {}): void {
    target.dispatchEvent(drag('drop', { data: { [BlockMime]: id } }, keys).event);
  }

  describe('dragging a block', () => {
    it('puts its id on the drag, so the drop knows which block it is', () => {
      const { cell } = render();
      const tile = cell(1, 1).querySelector('[draggable=true]') as HTMLElement;
      const start = drag('dragstart', {});
      tile.dispatchEvent(start.event);

      expect(start.data[BlockMime]).toBe('drive1');
      expect(start.dataTransfer.effectAllowed).toBe('all');
    });

    it('moves it onto an empty cell, and the focus goes with it', async () => {
      const { cell, settle, calls, host } = render();
      dropBlock(cell(0, 3), 'drive1');
      await settle();

      expect(calls).toEqual(['move drive1 0:3']);
      expect(host.querySelector('[data-focused]')?.id).toBe('cell-0-3');
    });

    it('moves it onto a shunt: the shunt only holds the place', async () => {
      const { cell, settle, calls } = render();
      dropBlock(cell(0, 2), 'drive1');
      await settle();

      expect(calls).toEqual(['move drive1 0:2']);
    });

    it('copies it with ⌥: one call places a block of its type with its parameters and its assets', async () => {
      const { cell, settle, calls, live } = render();
      dropBlock(cell(0, 3), 'cab1', { altKey: true });
      await settle();

      expect(calls).toEqual(['add cab 0:3 {"A":{"mic":2}} {"ir":"ir-42"}']);
      expect(live.selectedId()).toBe('cab9');
    });

    it('sends only the channels that hold values when copying', async () => {
      const { cell, settle, calls } = render();
      dropBlock(cell(0, 3), 'drive1', { altKey: true });
      await settle();

      expect(calls).toEqual(['add drive 0:3 {"A":{"gain":7,"tone":4},"B":{"gain":3}} {}']);
    });

    it('swaps it with the block it is dropped on, and replaces that block with ⌘ or Ctrl', async () => {
      const { cell, settle, calls } = render();
      dropBlock(cell(1, 2), 'drive1');
      dropBlock(cell(0, 1), 'drive1', { metaKey: true });
      dropBlock(cell(0, 1), 'drive2', { ctrlKey: true });
      await settle();

      expect(calls).toEqual(['swap drive1 drive2', 'replace drive1 delay1', 'replace drive2 delay1']);
    });

    it('does nothing when dropped back on itself, or for an id the grid does not have', async () => {
      const { cell, settle, calls } = render();
      dropBlock(cell(1, 1), 'drive1');
      dropBlock(cell(0, 3), 'gone1');
      await settle();

      expect(calls).toEqual([]);
    });

    it('never copies or replaces the grid Input: ⌥ moves it, ⌘ onto a block does nothing', async () => {
      const { cell, settle, calls } = render();
      dropBlock(cell(0, 3), 'in1', { altKey: true });
      dropBlock(cell(1, 1), 'in1', { metaKey: true });
      dropBlock(cell(0, 0), 'drive1', { metaKey: true });
      await settle();

      expect(calls).toEqual(['move in1 0:3']);
    });

    it('shows what a drop would do while dragging over a cell', () => {
      const { cell } = render();
      const over = (target: HTMLElement, keys: Parameters<typeof drag>[2] = {}) => {
        const { event, dataTransfer } = drag('dragover', { types: [BlockMime] }, keys);
        target.dispatchEvent(event);
        return event.defaultPrevented ? dataTransfer.dropEffect : 'refused';
      };

      expect(over(cell(0, 3))).toBe('move');
      expect(over(cell(0, 3), { altKey: true })).toBe('copy');
      expect(over(cell(0, 2))).toBe('move'); // a shunt
      expect(over(cell(1, 2))).toBe('link'); // a swap
      expect(over(cell(1, 2), { metaKey: true })).toBe('copy'); // a replacement
    });

    it('accepts files and refuses a drag it does not know', () => {
      const { cell } = render();
      const files = drag('dragover', { types: ['Files'] });
      cell(0, 3).dispatchEvent(files.event);
      expect(files.event.defaultPrevented).toBe(true);
      expect(files.dataTransfer.dropEffect).toBe('copy');

      const text = drag('dragover', { types: ['text/plain'] });
      cell(0, 3).dispatchEvent(text.event);
      expect(text.event.defaultPrevented).toBe(false);
    });
  });

  describe('dropping Library files', () => {
    const file = (name: string) => new File(['{}'], name);

    it('imports them and adds the first entry as a new block on an empty cell, with its settings', async () => {
      const { cell, settle, calls, live, imported } = render();
      imported.push({ id: 'e1', type: 'reverb' }, { id: 'e2', type: 'delay' });
      cell(0, 3).dispatchEvent(drag('drop', { files: [file('Hall.cthblock')] }).event);
      await settle();

      expect(calls).toEqual(['import Hall.cthblock', 'add reverb 0:3', 'apply e1 reverb9 {"all":true}']);
      expect(live.selectedId()).toBe('reverb9');
    });

    it('recalls the entry into the block it is dropped on when the types match', async () => {
      const { cell, settle, calls, live, imported } = render();
      imported.push({ id: 'e1', type: 'drive' });
      cell(1, 2).dispatchEvent(drag('drop', { files: [file('Crunch.cthblock')] }).event);
      await settle();

      expect(calls).toEqual(['import Crunch.cthblock', 'apply e1 drive2 {"all":true}']);
      expect(live.selectedId()).toBe('drive2');
    });

    it('only imports when dropped on a block of another type, or when the import brings nothing back', async () => {
      const { cell, settle, calls, imported } = render();
      cell(1, 2).dispatchEvent(drag('drop', { files: [file('Empty.cthblock')] }).event);
      await settle();
      imported.push({ id: 'e1', type: 'reverb' });
      cell(1, 2).dispatchEvent(drag('drop', { files: [file('Hall.cthblock')] }).event);
      await settle();

      expect(calls).toEqual(['import Empty.cthblock', 'import Hall.cthblock']);
    });

    it('ignores files that are not Library block files', async () => {
      const { cell, settle, calls } = render();
      cell(0, 3).dispatchEvent(drag('drop', { files: [file('song.wav'), file('Lead.cthpreset')] }).event);
      await settle();

      expect(calls).toEqual([]);
    });
  });

  describe('copy, cut and paste', () => {
    it('pastes a copied block onto an empty cell: its type, its parameters and its assets', async () => {
      const { press, settle, calls, live } = render();
      press('ArrowDown');
      press('ArrowLeft'); // the cab
      press('c', { metaKey: true });
      press('ArrowUp');
      press('ArrowRight');
      press('ArrowRight');
      press('ArrowRight'); // row 1, column 4: empty
      press('v', { metaKey: true });
      await settle();

      expect(calls).toEqual(['add cab 0:3 {"A":{"mic":2}} {"ir":"ir-42"}']);
      expect(live.selectedId()).toBe('cab9');
    });

    it('pastes onto a shunt like onto an empty cell', async () => {
      const { press, settle, calls } = render();
      press('ArrowDown'); // drive1
      press('c', { metaKey: true });
      press('ArrowUp');
      press('ArrowRight'); // the shunt
      press('v', { metaKey: true });
      await settle();

      expect(calls).toEqual(['add drive 0:2 {"A":{"gain":7,"tone":4},"B":{"gain":3}} {}']);
    });

    it('pastes the settings, every channel, onto a block of the same type', async () => {
      const { press, settle, calls } = render();
      press('ArrowDown'); // drive1
      press('c', { metaKey: true });
      press('ArrowRight'); // drive2
      press('v', { metaKey: true });
      await settle();

      expect(calls).toEqual(['settings drive2 drive {"A":{"gain":7,"tone":4},"B":{"gain":3}} {}']);
    });

    it('pastes nothing onto a block of another type, or before anything was copied', async () => {
      const { press, settle, calls } = render();
      press('v', { metaKey: true }); // nothing copied yet, on the delay
      press('ArrowDown'); // drive1
      press('c', { metaKey: true });
      press('ArrowUp'); // the delay
      press('v', { metaKey: true });
      await settle();

      expect(calls).toEqual([]);
    });

    it('cuts: removes the block and keeps it to paste', async () => {
      const { press, settle, calls } = render();
      press('ArrowDown'); // drive1
      press('x', { metaKey: true });
      press('ArrowUp');
      press('ArrowRight');
      press('ArrowRight'); // empty
      press('v', { metaKey: true });
      await settle();

      expect(calls).toEqual(['remove drive1', 'add drive 0:3 {"A":{"gain":7,"tone":4},"B":{"gain":3}} {}']);
    });

    it('copies neither the grid Input nor a shunt', async () => {
      const { press, settle, calls } = render();
      press('ArrowLeft'); // in1
      press('c', { metaKey: true });
      press('x', { metaKey: true });
      press('ArrowRight');
      press('ArrowRight'); // the shunt
      press('c', { metaKey: true });
      press('ArrowRight'); // empty
      press('v', { metaKey: true });
      await settle();

      expect(calls).toEqual([]);
    });

    it('pastes only while the grid has the focus', async () => {
      const { press, settle, calls, grid } = render();
      press('ArrowDown');
      press('c', { metaKey: true });
      press('ArrowRight'); // drive2: a paste would copy the settings
      const elsewhere = Object.assign(document.createElement('div'), { tabIndex: -1 });
      document.body.append(elsewhere);
      elsewhere.focus();
      const paste = new KeyboardEvent('keydown', { key: 'v', metaKey: true, bubbles: true, cancelable: true });
      elsewhere.dispatchEvent(paste);
      TestBed.inject(ShortcutService).dispatch(paste);
      await settle();

      expect(calls).toEqual([]);
      expect(grid.contains(document.activeElement)).toBe(false);
    });
  });

  describe('wiring with the pointer', () => {
    const pointer = (type: string, x: number, y: number) =>
      new PointerEvent(type, { bubbles: true, cancelable: true, clientX: x, clientY: y, pointerId: 1, button: 0 });

    /** The middle of a cell's left edge, where a cable arrives (jsdom lays nothing out: client = grid coordinates). */
    const inletOf = (row: number, column: number) => {
      const point = inlet({ row, column });
      return [point.x + 4, point.y] as const;
    };

    /** Drags a cable from a block's outlet and releases it at a point. */
    const drawFrom =
      ({ host, grid, fixture }: ReturnType<typeof render>) =>
      (outlet: string, x: number, y: number) => {
        host.querySelector(`[data-testid=outlet-${outlet}]`)?.dispatchEvent(pointer('pointerdown', 0, 0));
        fixture.detectChanges();
        grid.dispatchEvent(pointer('pointerup', x, y));
        fixture.detectChanges();
      };

    it('cables the block it starts from to the block it is released on', () => {
      const { host, grid, fixture, calls } = render();
      host.querySelector('[data-testid=outlet-cab1]')?.dispatchEvent(pointer('pointerdown', 0, 0));
      fixture.detectChanges();
      grid.dispatchEvent(pointer('pointermove', ...inletOf(1, 1)));
      fixture.detectChanges();
      expect(host.querySelector('[data-testid=cell-1-1]')?.getAttribute('data-wire')).toBe('target');
      expect(host.querySelector('[data-testid=cell-0-1]')?.getAttribute('data-wire')).toBe('candidate');
      expect(host.querySelector('[data-testid=cell-0-0]')?.hasAttribute('data-wire')).toBe(false);

      grid.dispatchEvent(pointer('pointerup', ...inletOf(1, 1)));
      fixture.detectChanges();
      expect(calls).toEqual(['cable cab1>drive1']);
      expect(host.querySelector('[data-wire]')).toBeNull();
    });

    it('lands nothing when released where it cannot connect: its own column, an existing cable, empty space', () => {
      const rendered = render();
      const attempt = drawFrom(rendered);
      attempt('drive1', ...inletOf(0, 1)); // the same column
      attempt('in1', ...inletOf(0, 1)); // the cable is there already
      attempt('drive1', ...inletOf(0, 3)); // an empty cell
      attempt('drive1', 5_000, 5_000); // outside the grid
      expect(rendered.calls).toEqual([]);
    });

    it('carries a cable across a shunt to a later column, but not across a block', () => {
      const rendered = render();
      rendered.live.preset.update(p => ({ ...p, blocks: [...p.blocks, block('reverb1', 'reverb', 0, 3)] }));
      rendered.fixture.detectChanges();
      const attempt = drawFrom(rendered);
      attempt('delay1', ...inletOf(0, 3)); // over the shunt
      attempt('in1', ...inletOf(1, 2)); // over the delay and the drive
      expect(rendered.calls).toEqual(['cable delay1>reverb1']);
    });

    it('offers no outlet on a shunt a cable already leaves, nor on an Output', () => {
      const { host, live, fixture } = render();
      expect(host.querySelector('[data-testid=outlet-shunt1]')).not.toBeNull();
      live.preset.update(p => ({
        ...p,
        blocks: [...p.blocks, block('out1', 'output', 0, 3), block('out2', 'output2', 1, 3)],
        cables: [...p.cables, { from: 'shunt1', to: 'out1' }]
      }));
      fixture.detectChanges();

      expect(host.querySelector('[data-testid=outlet-shunt1]')).toBeNull();
      expect(host.querySelector('[data-testid=outlet-out1]')).toBeNull();
      expect(host.querySelector('[data-testid=outlet-out2]')).toBeNull();
    });
  });
});
