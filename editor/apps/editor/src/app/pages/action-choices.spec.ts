import { type ActionContext, freshAction, targetChoices, valueChoices } from './action-choices';
import { targetLabel } from './action-picker';

const context: ActionContext = {
  blocks: [
    { id: 'amp1', name: 'Amp' },
    { id: 'delay1', name: 'Delay' }
  ],
  banks: [
    { id: 'factory', name: 'Factory', slots: ['factory-clean'] },
    { id: '3f9a0c', name: 'Friday gig', slots: [null, 'p-lead'] }
  ],
  presetName: id => ({ 'factory-clean': 'Clean', 'p-lead': 'Lead' })[id] ?? id,
  layouts: [{ id: 'layout-k3', name: 'Pedalboard' }],
  views: ['Scenes', 'Effects']
};

describe('action targets', () => {
  it('lists blocks, banks and layouts by name, storing their ids', () => {
    expect(targetChoices('bypass', context, null)).toEqual([
      { label: 'Amp (amp1)', value: 'amp1' },
      { label: 'Delay (delay1)', value: 'delay1' }
    ]);
    expect(targetChoices('presetSlot', context, null)?.map(c => [c.label, c.value])).toEqual([
      ['Factory', 'factory'],
      ['Friday gig', '3f9a0c']
    ]);
    expect(targetChoices('layout', context, null)).toEqual([{ label: 'Pedalboard', value: 'layout-k3' }]);
    expect(targetChoices('looper', context, null)?.[0]).toEqual({ label: 'record', value: 'record' });
    expect(targetChoices('tuner', context, null)).toBeNull();
  });

  it('keeps a stored target the lists do not hold, so it shows and stays', () => {
    expect(targetChoices('bypass', context, 'chorus1')?.at(-1)).toEqual({
      label: 'chorus1 (not found)',
      value: 'chorus1'
    });
  });

  it('names a stored target for the mappings table', () => {
    expect(targetLabel('presetSlot', '3f9a0c', context)).toBe('Friday gig');
    expect(targetLabel('bypass', 'gone1', context)).toBe('gone1');
    expect(targetLabel('tuner', null, context)).toBe('');
  });
});

describe('action values', () => {
  it('counts scenes, channels, Manuals and views as people do, storing what the engine reads', () => {
    expect(valueChoices('scene', context, null, null)?.slice(0, 3)).toEqual([
      { label: '(from the message)', value: null },
      { label: 'Scene 1', value: 0 },
      { label: 'Scene 2', value: 1 }
    ]);
    expect(valueChoices('channel', context, 'amp1', null)?.[1]).toEqual({ label: 'Channel B', value: 1 });
    expect(valueChoices('manual', context, null, null)?.[3]).toEqual({ label: 'Manual D', value: 3 });
    expect(valueChoices('external', context, null, null)?.[0]).toEqual({ label: 'External 1', value: 1 });
    expect(valueChoices('view', context, null, null)).toEqual([
      { label: 'Scenes', value: 0 },
      { label: 'Effects', value: 1 }
    ]);
    expect(valueChoices('bypass', context, 'amp1', null)).toBeNull();
  });

  it('lists a bank slot with the preset in it', () => {
    const slots = valueChoices('presetSlot', context, '3f9a0c', null);
    expect(slots).toHaveLength(129);
    expect(slots?.[1]).toEqual({ label: 'Slot 1', value: 0 });
    expect(slots?.[2]).toEqual({ label: 'Slot 2: Lead', value: 1 });
  });
});

describe('a freshly picked action', () => {
  it('starts on the first target and value; a mapping lets the message pick the scene or slot', () => {
    expect(freshAction('channel', context)).toEqual({ action: 'channel', target: 'amp1', value: 0 });
    expect(freshAction('scene', context)).toEqual({ action: 'scene', target: null, value: 0 });
    expect(freshAction('scene', context, true)).toEqual({ action: 'scene', target: null, value: null });
    expect(freshAction('presetSlot', context, true)).toEqual({ action: 'presetSlot', target: 'factory', value: null });
    expect(freshAction('tuner', context)).toEqual({ action: 'tuner', target: null, value: null });
  });
});
