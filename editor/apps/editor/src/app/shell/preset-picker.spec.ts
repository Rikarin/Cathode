import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ConnectionService } from '../core/connection';
import { Notify } from '../core/notify';
import { LibraryStore } from '../state/library.store';
import { LiveStore } from '../state/live.store';
import { PresetSwitcher } from '../state/preset-switcher';
import { UiStore } from '../state/ui.store';
import type { PickerEntry } from './picker-list';
import { PresetPicker, parseSlot } from './preset-picker';

describe('parseSlot', () => {
  it('reads a slot number as a foot controller counts them, from 1 to 128', () => {
    expect(parseSlot('1')).toBe(0);
    expect(parseSlot(' 12 ')).toBe(11);
    expect(parseSlot('128')).toBe(127);
  });

  it('takes anything else as a search', () => {
    for (const text of ['', '0', '129', '1000', '12a', 'lead', '1 2', '-3']) {
      expect(parseSlot(text)).toBeNull();
    }
  });
});

describe('PresetPicker', () => {
  const presets = [
    { id: 'p1', name: 'Clean', author: 'Ana', tags: ['funk'], factory: false, favorite: true, color: 'red' },
    { id: 'p2', name: 'Lead', author: null, tags: [], factory: true, favorite: false, color: null },
    { id: 'p3', name: '', author: null, tags: [], factory: false, favorite: false, color: null }
  ];
  const banks = [
    { id: 'factory', name: 'Factory', slots: [null, 'p2', 'gone'] },
    { id: 'mine', name: 'Mine', slots: ['p1'] },
    { id: 'empty', name: 'Empty', slots: [] }
  ];

  function render(loads = true, neighbour: object | Error = { presetId: 'p2', scene: 1, position: null }) {
    const presetPicker = signal(false);
    const live = { preset: signal<{ id: string; name: string } | undefined>({ id: 'p1', name: 'Clean' }) };
    const library = {
      presets: signal(presets),
      banks: signal(banks),
      refreshPresets: vi.fn(() => Promise.resolve()),
      refreshBanks: vi.fn(() => Promise.resolve())
    };
    const load = vi.fn(() => Promise.resolve(loads));
    const get = vi.fn(() =>
      neighbour instanceof Error
        ? Promise.reject(neighbour)
        : Promise.resolve({ data: neighbour, response: new Response(null, { status: 200 }) })
    );
    const error = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        { provide: UiStore, useValue: { presetPicker } },
        { provide: LiveStore, useValue: live },
        { provide: LibraryStore, useValue: library },
        { provide: PresetSwitcher, useValue: { load } },
        { provide: ConnectionService, useValue: { client: { GET: get } } },
        {
          provide: Notify,
          useValue: {
            error,
            run: async (action: () => Promise<unknown>) => {
              try {
                return await action();
              } catch (failure) {
                error(failure);
                return undefined;
              }
            }
          }
        }
      ]
    });
    TestBed.overrideComponent(PresetPicker, { set: { template: '', imports: [] } });
    const fixture = TestBed.createComponent(PresetPicker);
    fixture.detectChanges();
    const picker = fixture.componentInstance as unknown as {
      pinned: { set(on: boolean): void };
      query: { (): string; set(text: string): void };
      bankId: { set(id: string | null): void };
      bank(): { id: string } | null;
      entries(): PickerEntry[];
      slotText(): string | null;
      load(entry: PickerEntry): Promise<void>;
      loadSlot(): Promise<void>;
      loadBank(id: string): Promise<void>;
      step(direction: 1 | -1): Promise<void>;
      reload(): Promise<void>;
    };
    const open = () => {
      presetPicker.set(true);
      fixture.detectChanges();
    };
    return { picker, open, presetPicker, live, library, load, get, error };
  }

  it('lists each preset with its author or Factory and its tags', () => {
    const { picker } = render();
    expect(picker.entries()).toEqual([
      { id: 'p1', name: 'Clean', detail: 'Ana · funk', color: 'red', favorite: true },
      { id: 'p2', name: 'Lead', detail: 'Factory', color: null, favorite: false },
      { id: 'p3', name: '(untitled)', detail: '', color: null, favorite: false }
    ]);
  });

  it('opens on an empty search with the presets and banks read again', () => {
    const { picker, open, presetPicker, library } = render();
    open();
    picker.query.set('lead');

    presetPicker.set(false);
    open();

    expect(picker.query()).toBe('');
    expect(library.refreshPresets).toHaveBeenCalledTimes(2);
    expect(library.refreshBanks).toHaveBeenCalledTimes(2);
  });

  it('closes after a load, unless pinned or the load was refused', async () => {
    const entry = { id: 'p2', name: 'Lead' };
    const loaded = render();
    loaded.open();
    await loaded.picker.load(entry);
    expect(loaded.load).toHaveBeenCalledWith('p2', 'Lead');
    expect(loaded.presetPicker()).toBe(false);
    TestBed.resetTestingModule();

    const pinned = render();
    pinned.open();
    pinned.picker.pinned.set(true);
    await pinned.picker.load(entry);
    expect(pinned.presetPicker()).toBe(true);
    TestBed.resetTestingModule();

    // The user kept the unsaved edits: the picker stays, to choose again or close.
    const refused = render(false);
    refused.open();
    await refused.picker.load(entry);
    expect(refused.presetPicker()).toBe(true);
  });

  it('reads a number typed as a slot of the chosen bank, the first bank until one is chosen', () => {
    const { picker } = render();
    expect(picker.bank()?.id).toBe('factory');
    expect(picker.slotText()).toBeNull();

    picker.query.set('2');
    expect(picker.slotText()).toBe('Factory, slot 2: Lead');
    picker.query.set('1');
    expect(picker.slotText()).toBe('Factory, slot 1: empty');
    // A slot whose preset is gone from the library shows its id.
    picker.query.set('3');
    expect(picker.slotText()).toBe('Factory, slot 3: gone');

    picker.bankId.set('mine');
    picker.query.set('1');
    expect(picker.slotText()).toBe('Mine, slot 1: Clean');
    picker.query.set('lead');
    expect(picker.slotText()).toBeNull();
  });

  it('loads a slot remembering where it is, so next and previous go on through the bank', async () => {
    const { picker, open, presetPicker, load } = render();
    open();
    picker.query.set('2');

    await picker.loadSlot();

    expect(load).toHaveBeenCalledWith('p2', 'Lead', false, {
      presetId: 'p2',
      scene: null,
      position: { bank: 'factory', slot: 1 }
    });
    expect(presetPicker()).toBe(false);
  });

  it('says an empty slot is empty, and loads nothing', async () => {
    const { picker, open, presetPicker, load, error } = render();
    open();
    picker.query.set('1');

    await picker.loadSlot();

    expect(error).toHaveBeenCalledWith('Slot 1 of Factory is empty.');
    expect(load).not.toHaveBeenCalled();
    expect(presetPicker()).toBe(true);
  });

  it('plays a bank from its first filled slot', async () => {
    const { picker, load, error } = render();

    await picker.loadBank('factory');
    expect(load).toHaveBeenCalledWith('p2', 'Lead', false, {
      presetId: 'p2',
      scene: null,
      position: { bank: 'factory', slot: 1 }
    });

    await picker.loadBank('empty');
    expect(error).toHaveBeenCalledWith('Empty has no presets.');
    await picker.loadBank('nowhere');
    expect(error).toHaveBeenCalledWith('That bank has no presets.');
    expect(load).toHaveBeenCalledTimes(1);
  });

  it('steps to the neighbour the Server names, by its name in the library read again', async () => {
    const { picker, load, get, library } = render();

    await picker.step(-1);

    expect(get).toHaveBeenCalledWith('/api/v1/live/neighbour', { params: { query: { step: -1 } } });
    expect(library.refreshPresets).toHaveBeenCalledTimes(1);
    expect(load).toHaveBeenCalledWith('p2', 'Lead', false, { presetId: 'p2', scene: 1, position: null });
  });

  it('loads nothing when the Server cannot say which preset is next', async () => {
    const { picker, load, error } = render(true, new Error('offline'));

    await picker.step(1);

    expect(error).toHaveBeenCalledTimes(1);
    expect(load).not.toHaveBeenCalled();
  });

  it('reloads the saved preset, and says so when there is none', async () => {
    const { picker, live, load, error } = render();

    await picker.reload();
    expect(load).toHaveBeenCalledWith('p1', 'Clean');

    live.preset.set({ id: 'new', name: 'Untitled' });
    await picker.reload();
    expect(error).toHaveBeenCalledWith('This preset has not been saved yet: there is nothing to reload.');
    expect(load).toHaveBeenCalledTimes(1);
  });
});
