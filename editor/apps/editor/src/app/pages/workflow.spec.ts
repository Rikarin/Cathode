import type { BlockPreset, FootswitchLayout } from '@cathode/api-client';
import { TagColors, pickerEntries, tagColorName } from '../shell/picker-list';
import { parseSlot } from '../shell/preset-picker';
import { libraryFolders, wholeBlock } from '../state/block-presets.store';
import { nextChannel } from '../state/ui.store';
import { actionText, addView, overrideOf, removeView, viewNames, withOverride } from './footswitches';
import { movedControl, resolveControls, withControl, withLabel, withoutControl } from './perform';

describe('footswitch views and overrides', () => {
  const layout: FootswitchLayout = {
    id: 'main',
    name: 'Main',
    switches: [
      {
        index: 1,
        label: '1',
        message: 'cc',
        channel: 1,
        number: 1,
        tap: { action: 'tap' },
        hold: null,
        led: true,
        view: 0
      },
      {
        index: 2,
        label: '2',
        message: 'cc',
        channel: 1,
        number: 2,
        tap: { action: 'tap' },
        hold: null,
        led: true,
        view: 1
      },
      {
        index: 3,
        label: '3',
        message: 'cc',
        channel: 1,
        number: 3,
        tap: { action: 'tap' },
        hold: null,
        led: true,
        view: 2
      }
    ],
    views: ['A', 'B', 'C']
  };

  it('adds and removes views, moving later switches down', () => {
    expect(viewNames({ ...layout, views: null })).toEqual(['Main']);
    expect(addView(layout).views).toEqual(['A', 'B', 'C', 'View 4']);
    const removed = removeView(layout, 1);
    expect(removed.views).toEqual(['A', 'C']);
    expect(removed.switches.map(s => [s.index, s.view])).toEqual([
      [1, 0],
      [3, 1]
    ]);
  });

  it('keeps one override per switch in the preset', () => {
    let overrides = withOverride([], 'main', 1, { tap: { action: 'scene', value: 3 } });
    overrides = withOverride(overrides, 'main', 1, { tap: { action: 'scene', value: 4 } });
    expect(overrides).toHaveLength(1);
    expect(overrideOf(overrides, 'main', 1)?.tap.value).toBe(4);
    expect(withOverride(overrides, 'main', 1, { tap: null })).toEqual([]);
  });

  it('give an overridden switch a hold action and a label of its own, kept when the tap changes', () => {
    const tap = { action: 'scene', value: 0 };
    let overrides = withOverride([], 'main', 2, { tap });
    overrides = withOverride(overrides, 'main', 2, { hold: { action: 'tuner' } });
    overrides = withOverride(overrides, 'main', 2, { label: '  Verse  ' });
    overrides = withOverride(overrides, 'main', 2, { tap: { action: 'scene', value: 1 } });
    expect(overrides).toEqual([
      { layout: 'main', index: 2, tap: { action: 'scene', value: 1 }, hold: { action: 'tuner' }, label: 'Verse' }
    ]);

    // Blank text goes back to the layout's label; a null hold means none in this preset.
    overrides = withOverride(overrides, 'main', 2, { label: ' ', hold: null });
    expect(overrideOf(overrides, 'main', 2)).toMatchObject({ hold: null, label: null });
  });

  it('need a tap before a hold or label, and keep an override in its place', () => {
    expect(withOverride([], 'main', 1, { label: 'Solo' })).toEqual([]);
    const overrides = [
      { layout: 'main', index: 1, tap: { action: 'tap' }, hold: null, label: null },
      { layout: 'main', index: 2, tap: { action: 'tuner' }, hold: null, label: null }
    ];
    expect(withOverride(overrides, 'main', 1, { label: 'Tap' }).map(o => o.label)).toEqual(['Tap', null]);
  });

  it('show scene, channel, Manual and view numbers from 1, as Externals are, stored from 0', () => {
    expect(actionText({ action: 'scene', value: 0 })).toBe('scene 1');
    expect(actionText({ action: 'channel', target: 'amp1', value: 1 })).toBe('channel amp1 2');
    expect(actionText({ action: 'manual', value: 3 })).toBe('manual 4');
    expect(actionText({ action: 'view', value: 0 })).toBe('view 1');
    expect(actionText({ action: 'external', value: 1 })).toBe('external 1');
    expect(actionText({ action: 'bypass', target: 'delay1', value: null })).toBe('bypass delay1');
  });
});

describe('performance pages', () => {
  it('add a control once and remove it', () => {
    const page = withControl(withControl([], { block: 'amp1', param: 'gain' }), { block: 'amp1', param: 'gain' });
    expect(page).toHaveLength(1);
    expect(withoutControl(page, { block: 'amp1', param: 'gain' })).toEqual([]);
  });

  it('give a control a caption of its own, blank going back to the parameter name', () => {
    const page = [
      { block: 'amp1', param: 'gain', label: null },
      { block: 'delay1', param: 'mix', label: null }
    ];
    const named = withLabel(page, page[0] as never, '  Crunch ');
    expect(named.map(c => c.label)).toEqual(['Crunch', null]);
    expect(withLabel(named, page[0] as never, '   ')[0]?.label).toBeNull();
    const info = () => ({ name: 'Amp', params: [{ id: 'gain', name: 'Gain' }] }) as never;
    const preset = { blocks: [{ id: 'amp1', type: 'amp' }] } as never;
    expect(resolveControls(named, preset, info)[0]).toMatchObject({ label: 'Crunch', name: 'Amp Gain' });
  });

  it('move a control to another place, the others keeping their order', () => {
    const page = ['a', 'b', 'c', 'd'].map(block => ({ block, param: 'p' }));
    expect(movedControl(page, 0, 2).map(c => c.block)).toEqual(['b', 'c', 'a', 'd']);
    expect(movedControl(page, 3, 0).map(c => c.block)).toEqual(['d', 'a', 'b', 'c']);
    expect(movedControl(page, 1, 99).map(c => c.block)).toEqual(['a', 'c', 'd', 'b']);
    expect(movedControl(page, 7, 0)).toEqual(page);
  });

  it('show a global control as missing in a preset without its block', () => {
    const resolved = resolveControls([{ block: 'amp1', param: 'gain' }], { blocks: [] } as never, () => undefined);
    expect(resolved[0]?.block).toBeNull();
    expect(resolved[0]?.label).toBe('amp1 gain');
  });
});

describe('pickers', () => {
  it('search every word, favourites first, then by name', () => {
    const entries = [
      { id: '1', name: 'Clean Twin', favorite: false },
      { id: '2', name: 'Crunch Plexi', detail: 'rock', favorite: true },
      { id: '3', name: 'Clean Vox', favorite: true }
    ];
    expect(pickerEntries(entries, '').map(e => e.id)).toEqual(['3', '2', '1']);
    expect(pickerEntries(entries, 'clean').map(e => e.id)).toEqual(['3', '1']);
    expect(pickerEntries(entries, 'plexi rock').map(e => e.id)).toEqual(['2']);
    expect(pickerEntries(entries, '', true).map(e => e.id)).toEqual(['3', '2']);
  });

  it('read a typed bank slot, 1 to 128', () => {
    expect(parseSlot('12')).toBe(11);
    expect(parseSlot(' 1 ')).toBe(0);
    expect(parseSlot('0')).toBeNull();
    expect(parseSlot('129')).toBeNull();
    expect(parseSlot('lead')).toBeNull();
  });

  it('name the colour tags as people say them', () => {
    expect(TagColors.map(tagColorName)).toEqual(['Red', 'Orange', 'Yellow', 'Green', 'Blue', 'Purple']);
    expect(tagColorName('teal')).toBe('teal');
  });
});

describe('channels and the Library', () => {
  it('step to the next channel, wrapping within the block’s channels', () => {
    expect(nextChannel('A', 4)).toBe('B');
    expect(nextChannel('D', 4)).toBe('A');
    expect(nextChannel('B', 2)).toBe('A');
  });

  it('group the user’s entries by folder, unfiled first', () => {
    const entry = (name: string, folder: string | null) =>
      ({ id: name, name, type: 'delay', factory: false, values: {}, folder }) as BlockPreset;
    const folders = libraryFolders([entry('b', 'Mod'), entry('a', null), entry('c', 'Amps'), entry('a2', 'Mod')]);
    expect(folders.map(f => [f.name, f.entries.map(e => e.name)])).toEqual([
      ['', ['a']],
      ['Amps', ['c']],
      ['Mod', ['a2', 'b']]
    ]);
  });

  it('save a whole block with its channels and modifiers, without the block id', () => {
    const block = {
      id: 'delay1',
      type: 'delay',
      row: 0,
      column: 2,
      channels: { A: { time: 300 }, B: { time: 500 } },
      assets: {},
      bypassMode: 'thru',
      sceneIgnore: []
    };
    const modifiers = [
      { block: 'delay1', param: 'mix', source: 'lfo1', min: 0, max: 1, curve: 'linear', attackMs: 0, releaseMs: 0 },
      { block: 'drive1', param: 'gain', source: 'lfo1', min: 0, max: 1, curve: 'linear', attackMs: 0, releaseMs: 0 }
    ];
    const entry = wholeBlock(block, 'B', modifiers);
    expect(entry.values).toEqual({ time: 500 });
    expect(entry.channels).toEqual(block.channels);
    expect(entry.modifiers).toEqual([{ ...modifiers[0], block: '' }]);
  });
});
