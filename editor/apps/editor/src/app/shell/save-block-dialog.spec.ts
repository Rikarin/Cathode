import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { BlockPreset, PresetBlock } from '@cathode/api-client';
import { BlockPresetsStore } from '../state/block-presets.store';
import { CatalogStore } from '../state/catalog.store';
import { LiveStore } from '../state/live.store';
import { UiStore } from '../state/ui.store';
import { SaveBlockDialog } from './save-block-dialog';

describe('SaveBlockDialog', () => {
  function render(stored: boolean) {
    const block = {
      id: 'd1',
      type: 'delay',
      channels: { A: { time: 300 }, B: { time: 450 } },
      assets: {}
    } as unknown as PresetBlock;
    const ui = { saveBlockOpen: signal(false), block: signal<PresetBlock | null>(block), channels: () => 2 };
    const saves: { type: string; name: string; entry: Partial<BlockPreset> }[] = [];
    const presets = {
      folders: signal([]),
      refresh: () => Promise.resolve(),
      save: (type: string, name: string, entry: Partial<BlockPreset>) => {
        saves.push({ type, name, entry });
        return Promise.resolve(stored);
      }
    };
    TestBed.configureTestingModule({
      providers: [
        { provide: UiStore, useValue: ui },
        { provide: BlockPresetsStore, useValue: presets },
        { provide: LiveStore, useValue: { channelOf: () => 'B', preset: () => ({ modifiers: [] }) } },
        { provide: CatalogStore, useValue: { info: () => ({ name: 'Delay', params: [{ id: 'time' }] }) } }
      ]
    });
    TestBed.overrideComponent(SaveBlockDialog, { set: { template: '', imports: [] } });
    const fixture = TestBed.createComponent(SaveBlockDialog);
    const dialog = fixture.componentInstance as unknown as {
      name: { (): string; set(value: string): void };
      folder: { (): string; set(value: string): void };
      scope: { set(value: 'channel' | 'block'): void };
      save(): Promise<void>;
    };
    ui.saveBlockOpen.set(true);
    fixture.detectChanges();
    return { ui, dialog, saves, fixture };
  }

  it('opens on the block type’s name, saving the current channel', async () => {
    const { dialog, saves } = render(true);
    expect(dialog.name()).toBe('Delay');

    dialog.folder.set(' Rock ');
    await dialog.save();

    expect(saves).toEqual([{ type: 'delay', name: 'Delay', entry: { values: { time: 450 }, folder: 'Rock' } }]);
  });

  it('saves the whole block when asked to', async () => {
    const { dialog, saves } = render(true);

    dialog.scope.set('block');
    await dialog.save();

    expect(saves[0]?.entry).toMatchObject({ channels: { A: { time: 300 }, B: { time: 450 } }, modifiers: [] });
  });

  it('closes once the entry is stored', async () => {
    const { ui, dialog } = render(true);

    await dialog.save();

    expect(ui.saveBlockOpen()).toBe(false);
  });

  it('stays open with the name and folder as typed when the save is refused (#264)', async () => {
    const { ui, dialog, fixture } = render(false);
    dialog.name.set('Slap');
    dialog.folder.set('a/b');

    await dialog.save();
    fixture.detectChanges();

    expect(ui.saveBlockOpen()).toBe(true);
    expect(dialog.name()).toBe('Slap');
    expect(dialog.folder()).toBe('a/b');
  });

  it('saves nothing without a name', async () => {
    const { ui, dialog, saves } = render(true);
    dialog.name.set('   ');

    await dialog.save();

    expect(saves).toEqual([]);
    expect(ui.saveBlockOpen()).toBe(true);
  });
});
