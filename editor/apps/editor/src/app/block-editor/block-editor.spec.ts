import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { BlockTypeInfo, ParameterInfo, PresetBlock } from '@cathode/api-client';
import { EventStream } from '../core/events';
import { BlockPresetsStore } from '../state/block-presets.store';
import { CatalogStore } from '../state/catalog.store';
import { GlobalStore } from '../state/global.store';
import { LibraryStore } from '../state/library.store';
import { LiveStore } from '../state/live.store';
import { PreferencesStore } from '../state/preferences.store';
import { UiStore } from '../state/ui.store';
import { BlockEditor } from './block-editor';

// The block page: which block it shows, which controls go on which page, and what the sidebar offers.
describe('BlockEditor', () => {
  const param = (id: string, more: Partial<ParameterInfo> = {}): ParameterInfo => ({
    id,
    name: id.charAt(0).toUpperCase() + id.slice(1),
    kind: 'float',
    min: 0,
    max: 10,
    default: 5,
    unit: '',
    taper: 'linear',
    visibleWhen: null,
    ...more
  });

  const catalog: Record<string, Partial<BlockTypeInfo>> = {
    delay: {
      name: 'Delay',
      category: 'delay',
      channels: 4,
      pages: ['Basic', 'Tone', 'Unused'],
      params: [
        param('type', { kind: 'enum', default: 0, options: ['Digital', 'Tape'] }),
        param('time'),
        param('feedback'),
        param('wow', { visibleWhen: { param: 'type', values: [1] } }),
        param('lowCut', { page: 'Tone' }),
        param('mix'),
        param('level')
      ]
    },
    circuitamp: {
      name: 'Amp',
      category: 'amp',
      channels: 4,
      pages: ['Basic'],
      params: [
        param('type', { kind: 'enum', default: 0, options: ['Lux', 'Plexi'] }),
        param('gain', { typeLabels: ['Volume', 'Loudness'] }),
        param('presence', { typeLabels: ['', 'Presence'] })
      ]
    },
    input: { name: 'Input', category: 'io', channels: 1, params: [param('level')] },
    output: { name: 'Output', category: 'io', channels: 1, params: [param('level')] }
  };

  function render(selected: Partial<PresetBlock> | null, values: Record<string, number> = {}) {
    vi.stubGlobal('requestAnimationFrame', () => 1);
    vi.stubGlobal('cancelAnimationFrame', () => undefined);
    const live = {
      selected: signal(selected as PresetBlock | null),
      channelOf: () => 'A',
      valueOf: (_block: PresetBlock, _channel: string, id: string) => values[id],
      modifierOf: vi.fn((_block: string, _param: string) => undefined as { source: string } | undefined),
      putModifier: vi.fn(() => Promise.resolve())
    };
    const blockPage = signal<Readonly<Record<string, string>>>({});
    const ampView = signal<'authentic' | 'ideal'>('authentic');
    const refresh = vi.fn(() => Promise.resolve());
    const library = [
      { id: 'f1', type: 'delay', name: 'Slap', factory: true },
      { id: 'u1', type: 'delay', name: 'Long', factory: false },
      { id: 'u2', type: 'reverb', name: 'Hall', factory: false }
    ];
    TestBed.configureTestingModule({
      providers: [
        { provide: LiveStore, useValue: live },
        { provide: EventStream, useValue: { blockMeter: () => undefined } },
        { provide: CatalogStore, useValue: { info: (type: string) => catalog[type] } },
        {
          provide: GlobalStore,
          useValue: {
            settings: signal({
              blocks: [
                { id: 'g1', type: 'delay', name: 'Shared echo' },
                { id: 'g2', type: 'reverb', name: 'Shared room' }
              ]
            })
          }
        },
        {
          provide: BlockPresetsStore,
          useValue: { refresh, forType: (type: string) => library.filter(p => p.type === type) }
        },
        // The page asks the Library only for the capture an Amp NAM block plays (whether it has sizes).
        { provide: LibraryStore, useValue: { models: signal([]) } },
        { provide: UiStore, useValue: { blockPage, saveBlockOpen: signal(false) } },
        { provide: PreferencesStore, useValue: { ampView } }
      ]
    });
    TestBed.overrideComponent(BlockEditor, { set: { template: '', imports: [] } });
    const fixture = TestBed.createComponent(BlockEditor);
    fixture.detectChanges();
    const names = (params: ParameterInfo[]) => params.map(p => p.name);
    const editor = fixture.componentInstance as unknown as {
      block(): PresetBlock | null;
      isIo(): boolean;
      title(): string;
      typeParam(): ParameterInfo | null;
      mainParams(): ParameterInfo[];
      outputParams(): ParameterInfo[];
      pages(): string[];
      page(): string | null;
      pageParams(): ParameterInfo[];
      onFirstPage(): boolean;
      showPage(name: string): void;
      libraryIds(): string[];
      libraryName(id: string): string;
      globalBlock(): { name: string } | undefined;
      sameTypeGlobals(): { id: string }[];
      ampView(): string;
      ampViewOverride: { set(view: 'authentic' | 'ideal'): void };
      engageModifier(): unknown;
      removeEngageModifier(): void;
      bypassModeName(mode: string): string;
    };
    return { editor, live, blockPage, ampView, refresh, names };
  }

  afterEach(() => vi.unstubAllGlobals());

  it('shows the selected block, but has nothing to show for a shunt or no selection', () => {
    expect(render(null).editor.block()).toBeNull();
    TestBed.resetTestingModule();
    expect(render({ id: 's1', type: 'shunt' }).editor.block()).toBeNull();
    TestBed.resetTestingModule();

    const { editor, refresh } = render({ id: 'delay1', type: 'delay' });
    expect(editor.block()?.id).toBe('delay1');
    expect(editor.title()).toBe('Delay');
    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it('falls back to the type id for a block the catalog does not know', () => {
    const { editor } = render({ id: 'x1', type: 'fromANewerServer' });
    expect(editor.title()).toBe('fromANewerServer');
    expect(editor.mainParams()).toEqual([]);
    expect(editor.pages()).toEqual([]);
    expect(editor.page()).toBeNull();
  });

  it('sets the type and the output stage apart from the block’s own controls', () => {
    const { editor, names } = render({ id: 'delay1', type: 'delay' });
    expect(editor.typeParam()?.id).toBe('type');
    expect(names(editor.mainParams())).toEqual(['Time', 'Feedback', 'LowCut']);
    expect(names(editor.outputParams())).toEqual(['Mix', 'Level']);
  });

  it('shows a control another setting hides only while that setting holds', () => {
    const { editor, names } = render({ id: 'delay1', type: 'delay' }, { type: 1 });
    expect(names(editor.mainParams())).toEqual(['Time', 'Feedback', 'Wow', 'LowCut']);
  });

  it('lists only the pages with a control to show, a control without a page going on the first', () => {
    const { editor, names } = render({ id: 'delay1', type: 'delay' });
    expect(editor.pages()).toEqual(['Basic', 'Tone']);
    expect(editor.page()).toBe('Basic');
    expect(editor.onFirstPage()).toBe(true);
    expect(names(editor.pageParams())).toEqual(['Time', 'Feedback']);
  });

  it('opens a block type on the page last shown for it, while that page still has controls', () => {
    const { editor, blockPage, names } = render({ id: 'delay1', type: 'delay' });

    editor.showPage('Tone');
    expect(blockPage()).toEqual({ delay: 'Tone' });
    expect(editor.page()).toBe('Tone');
    expect(editor.onFirstPage()).toBe(false);
    expect(names(editor.pageParams())).toEqual(['LowCut']);

    // A page remembered from before its controls went away: the first page shows.
    blockPage.set({ delay: 'Unused' });
    expect(editor.page()).toBe('Basic');
  });

  it('shows every control of a block with one page', () => {
    const { editor, names } = render({ id: 'in1', type: 'input' });
    expect(editor.pages()).toEqual([]);
    expect(editor.onFirstPage()).toBe(true);
    expect(names(editor.pageParams())).toEqual([]);
    expect(names(editor.outputParams())).toEqual(['Level']);
  });

  it('knows the grid’s Input and Output, which have no bypass and no global link', () => {
    expect(render({ id: 'in1', type: 'input' }).editor.isIo()).toBe(true);
    TestBed.resetTestingModule();
    expect(render({ id: 'out1', type: 'output' }).editor.isIo()).toBe(true);
    TestBed.resetTestingModule();
    expect(render({ id: 'delay1', type: 'delay' }).editor.isIo()).toBe(false);
  });

  it('names the Amp’s controls as the model does: its own panel, or every control in the Ideal view', () => {
    const { editor, ampView, names } = render({ id: 'amp1', type: 'circuitamp' });
    expect(editor.ampView()).toBe('authentic');
    expect(names(editor.mainParams())).toEqual(['Volume']);

    // The preference, until the page's own switch is used.
    ampView.set('ideal');
    expect(names(editor.mainParams())).toEqual(['Volume', 'Presence']);
    editor.ampViewOverride.set('authentic');
    expect(names(editor.mainParams())).toEqual(['Volume']);
  });

  it('names the Amp’s controls for the model selected', () => {
    const { editor, names } = render({ id: 'amp1', type: 'circuitamp' }, { type: 1 });
    expect(names(editor.mainParams())).toEqual(['Loudness', 'Presence']);
  });

  it('offers the Block Library entries of the block’s type, the user’s own marked', () => {
    const { editor } = render({ id: 'delay1', type: 'delay' });
    expect(editor.libraryIds()).toEqual(['f1', 'u1']);
    expect(editor.libraryName('f1')).toBe('Slap');
    expect(editor.libraryName('u1')).toBe('Long (mine)');
    expect(editor.libraryName('gone')).toBe('gone');
  });

  it('names the global block a linked block follows, and offers the globals of its type', () => {
    const { editor } = render({ id: 'delay1', type: 'delay', global: 'g1' });
    expect(editor.globalBlock()?.name).toBe('Shared echo');
    expect(editor.sameTypeGlobals().map(b => b.id)).toEqual(['g1']);
    TestBed.resetTestingModule();

    expect(render({ id: 'delay1', type: 'delay' }).editor.globalBlock()).toBeUndefined();
  });

  it('finds and removes the modifier that engages the block', () => {
    const { editor, live } = render({ id: 'delay1', type: 'delay' });
    live.modifierOf.mockReturnValue({ source: 'ext1' });
    expect(editor.engageModifier()).toEqual({ source: 'ext1' });
    expect(live.modifierOf).toHaveBeenCalledWith('delay1', 'engage');

    editor.removeEngageModifier();
    expect(live.putModifier).toHaveBeenCalledWith('delay1', 'engage', null);
  });

  it('names the bypass modes, and shows one it does not know by its id', () => {
    const { editor } = render({ id: 'delay1', type: 'delay' });
    expect(editor.bypassModeName('muteFxIn')).toBe('Mute FX In (spillover)');
    expect(editor.bypassModeName('somethingNew')).toBe('somethingNew');
  });
});
