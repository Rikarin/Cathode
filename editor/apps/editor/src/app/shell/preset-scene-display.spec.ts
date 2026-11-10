import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { LibraryStore } from '../state/library.store';
import { LiveStore } from '../state/live.store';
import { UiStore } from '../state/ui.store';
import { PresetSceneDisplay, presetBank, presetPlace } from './preset-scene-display';

const banks = [
  { id: 'factory', name: 'Factory', slots: ['f1', 'f2'] },
  { id: 'gig', name: 'Gig', slots: ['a', null, 'b', 'a'] }
];

describe('presetPlace', () => {
  it('numbers the slot or setlist entry the preset was loaded from, from 1', () => {
    expect(presetPlace({ bank: 'gig', slot: 3 }, 'a', banks)).toEqual({ number: '004', where: 'Gig, slot 4' });
    expect(presetPlace({ setlist: 's1', entry: 1 }, 'b', banks)).toEqual({
      number: '002',
      where: 'Setlist entry 2'
    });
  });

  it('finds the first slot holding a preset loaded by name, in bank order', () => {
    expect(presetPlace(null, 'a', banks)).toEqual({ number: '001', where: 'Gig, slot 1' });
    expect(presetPlace(undefined, 'f2', banks)).toEqual({ number: '002', where: 'Factory, slot 2' });
  });

  it('has no number for a preset in no bank', () => {
    expect(presetPlace(null, 'loose', banks)).toEqual({ number: '---', where: 'In no bank' });
    expect(presetPlace(null, undefined, banks)).toEqual({ number: '---', where: 'In no bank' });
  });
});

describe('presetBank', () => {
  it('is the bank the preset was loaded from, even when an earlier bank holds it too', () => {
    expect(presetBank({ bank: 'gig', slot: 0 }, 'a', banks)).toBe('gig');
    expect(presetBank({ bank: 'factory', slot: 1 }, 'f2', banks)).toBe('factory');
  });

  it('is the first bank holding a preset loaded by name', () => {
    expect(presetBank(null, 'b', banks)).toBe('gig');
    expect(presetBank(undefined, 'f1', banks)).toBe('factory');
  });

  it('is none for a setlist entry, a preset in no bank, or a bank since deleted', () => {
    expect(presetBank({ setlist: 's1', entry: 0 }, 'a', banks)).toBeNull();
    expect(presetBank(null, 'loose', banks)).toBeNull();
    expect(presetBank(null, undefined, banks)).toBeNull();
    expect(presetBank({ bank: 'gone', slot: 0 }, 'a', banks)).toBeNull();
  });
});

describe('PresetSceneDisplay rename', () => {
  function render() {
    const preset = signal({ id: 'p1', scenes: [{ name: 'Clean' }, { name: 'Crunch' }, { name: 'Lead' }] });
    const activeScene = signal(0);
    const renameScene = vi.fn((_scene: number, _name: string) => Promise.resolve(true));
    TestBed.configureTestingModule({
      providers: [
        { provide: LiveStore, useValue: { preset, activeScene, state: signal(undefined), renameScene } },
        { provide: UiStore, useValue: {} },
        { provide: LibraryStore, useValue: { banks: signal([]) } }
      ]
    });
    TestBed.overrideComponent(PresetSceneDisplay, { set: { template: '', imports: [] } });
    const fixture = TestBed.createComponent(PresetSceneDisplay);
    fixture.detectChanges();
    const display = fixture.componentInstance as unknown as {
      renaming: () => { scene: number; name: string } | null;
      startRename: () => void;
      rename: (name: string) => void;
    };
    return { display, preset, activeScene, renameScene };
  }

  it('renames the active scene', () => {
    const { display, activeScene, renameScene } = render();
    activeScene.set(1);
    display.startRename();
    expect(display.renaming()).toMatchObject({ scene: 1, name: 'Crunch' });

    display.rename('  Rhythm ');
    expect(renameScene).toHaveBeenCalledWith(1, 'Rhythm');
    expect(display.renaming()).toBeNull();
  });

  it('renames the scene the field was opened for when another scene has become active', () => {
    const { display, activeScene, renameScene } = render();
    display.startRename();
    activeScene.set(2);
    expect(display.renaming()).toMatchObject({ scene: 0, name: 'Clean' }); // the field keeps its scene's name

    display.rename('Intro');
    expect(renameScene).toHaveBeenCalledTimes(1);
    expect(renameScene).toHaveBeenCalledWith(0, 'Intro');
  });

  it('renames nothing for an unchanged or empty name, even if the scene now active has another name', () => {
    const { display, activeScene, renameScene } = render();
    display.startRename();
    activeScene.set(2);
    display.rename('Clean');
    display.startRename();
    display.rename('   ');
    expect(renameScene).not.toHaveBeenCalled();
  });

  it('renames nothing once another preset is loaded', () => {
    const { display, preset, renameScene } = render();
    display.startRename();
    preset.set({ id: 'p2', scenes: [{ name: 'Verse' }] });
    display.rename('Intro');
    expect(renameScene).not.toHaveBeenCalled();
    expect(display.renaming()).toBeNull();
  });

  it('renames only once when Enter is followed by the blur of the closing field', () => {
    const { display, renameScene } = render();
    display.startRename();
    display.rename('Intro');
    display.rename('Intro');
    expect(renameScene).toHaveBeenCalledTimes(1);
  });
});
