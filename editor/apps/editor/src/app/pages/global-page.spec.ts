import { AllMidiOutputs, describeMidi, midiOutputChoices, midiOutputName } from './global-page';

describe('describeMidi', () => {
  it('reads channel messages the way a guitarist would say them', () => {
    expect(describeMidi({ status: 0xb0, data1: 11, data2: 64 })).toBe('CC 11 = 64 (ch 1)');
    expect(describeMidi({ status: 0x93, data1: 60, data2: 100 })).toBe('Note on 60 vel 100 (ch 4)');
    expect(describeMidi({ status: 0x90, data1: 60, data2: 0 })).toBe('Note off 60 (ch 1)');
    expect(describeMidi({ status: 0xcf, data1: 5, data2: 0 })).toBe('Program 5 (ch 16)');
  });
});

describe('midiOutputChoices', () => {
  const outputs = [
    { uid: 'out-1', name: 'Pedalboard' },
    { uid: 'out-2', name: 'Synth' }
  ];

  it('offers every output, then each one, and keeps a saved port that is not connected', () => {
    expect(midiOutputChoices(outputs, null)).toEqual([AllMidiOutputs, 'out-1', 'out-2']);
    expect(midiOutputChoices(outputs, 'out-2')).toEqual([AllMidiOutputs, 'out-1', 'out-2']);
    expect(midiOutputChoices(outputs, 'gone')).toEqual([AllMidiOutputs, 'out-1', 'out-2', 'gone']);
  });

  it('names each choice', () => {
    expect(midiOutputName(outputs, AllMidiOutputs)).toBe('All outputs');
    expect(midiOutputName(outputs, 'out-2')).toBe('Synth');
    expect(midiOutputName(outputs, 'gone')).toBe('gone (not connected)');
  });
});
