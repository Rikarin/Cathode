import type { BlockPreset, FootswitchLayout } from '@cathode/api-client';
import { TagColors, pickerEntries, tagColorName } from '../shell/picker-list';
import { parseSlot } from '../shell/preset-picker';
import { libraryFolders, wholeBlock } from '../state/block-presets.store';
import { nextChannel } from '../state/ui.store';
import { actionText, addView, overrideOf, parseAction, removeView, viewNames, withOverride } from './footswitches';
import { resolveControls, withControl, withoutControl } from './perform';

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
    let overrides = withOverride([], 'main', 1, { action: 'scene', value: 3 });
    overrides = withOverride(overrides, 'main', 1, { action: 'scene', value: 4 });
    expect(overrides).toHaveLength(1);
    expect(overrideOf(overrides, 'main', 1)?.tap.value).toBe(4);
    expect(withOverride(overrides, 'main', 1, null)).toEqual([]);
  });

  it('show and take scene, channel, Manual and view numbers from 1, as Externals are, storing them from 0', () => {
    const actions = ['scene', 'channel', 'manual', 'view', 'external', 'bypass', 'tuner'];
    expect(actionText({ action: 'scene', value: 0 })).toBe('scene 1');
    expect(actionText({ action: 'channel', target: 'amp1', value: 1 })).toBe('channel amp1 2');
    expect(actionText({ action: 'manual', value: 3 })).toBe('manual 4');
    expect(actionText({ action: 'view', value: 0 })).toBe('view 1');
    expect(actionText({ action: 'external', value: 1 })).toBe('external 1');
    expect(actionText({ action: 'bypass', target: 'delay1', value: null })).toBe('bypass delay1');
    expect(parseAction('scene 3', actions)).toEqual({ action: 'scene', target: null, value: 2 });
    expect(parseAction('channel amp1 1', actions)).toEqual({ action: 'channel', target: 'amp1', value: 0 });
    expect(parseAction('external 2', actions)).toEqual({ action: 'external', target: null, value: 2 });
    expect(parseAction('scene 0', actions)?.value).toBe(0);
    expect(parseAction('tuner', actions)).toEqual({ action: 'tuner', target: null, value: null });
    expect(parseAction('louder', actions)).toBeNull();
    // What is stored round-trips through the text unchanged.
    for (const action of [
      { action: 'scene', target: null, value: 5 },
      { action: 'view', target: null, value: 1 }
    ]) {
      expect(parseAction(actionText(action), actions)).toEqual(action);
    }
  });
});

describe('performance pages', () => {
  it('add a control once and remove it', () => {
    const page = withControl(withControl([], { block: 'amp1', param: 'gain' }), { block: 'amp1', param: 'gain' });
    expect(page).toHaveLength(1);
    expect(withoutControl(page, { block: 'amp1', param: 'gain' })).toEqual([]);
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
