import type { ParameterInfo } from '@cathode/api-client';
import { blockDefault, defaultOn, isShown, labelledParams } from './type-labels';

function param(id: string, name: string, typeLabels?: string[]): ParameterInfo {
  return {
    id,
    name,
    kind: 'float',
    min: 0,
    max: 10,
    default: 5,
    unit: '',
    taper: 'linear',
    visibleWhen: null,
    ...(typeLabels ? { typeLabels } : {})
  };
}

describe('isShown', () => {
  // The Compressor: Threshold and Ratio with Controls on Threshold/Ratio, the one Compression knob otherwise.
  const controls: ParameterInfo = { ...param('controls', 'Controls'), kind: 'enum', max: 1, default: 0 };
  const threshold: ParameterInfo = {
    ...param('threshold', 'Threshold'),
    visibleWhen: { param: 'controls', values: [0] }
  };
  const compression: ParameterInfo = {
    ...param('compression', 'Compression'),
    visibleWhen: { param: 'controls', values: [1] }
  };
  const params = [controls, threshold, compression, param('attack', 'Attack')];
  const shown = (values: Record<string, number>) =>
    params.filter(p => isShown(params, p, id => values[id])).map(p => p.id);

  it('shows the knobs the other control’s setting uses and hides the rest', () => {
    expect(shown({ controls: 0 })).toEqual(['controls', 'threshold', 'attack']);
    expect(shown({ controls: 1 })).toEqual(['controls', 'compression', 'attack']);
  });

  it('reads an unset control as its default', () => {
    expect(shown({})).toEqual(['controls', 'threshold', 'attack']);
  });
});

describe('labelledParams', () => {
  const params = [
    param('gain', 'Gain'),
    param('cut', 'Cut', ['', '', 'Cut']),
    param('switch1', 'Switch 1', ['', 'Jumper', '']),
    param('control1', 'Control 1', ['Lead Drive', '', ''])
  ];

  it('names and hides each type’s controls', () => {
    expect(labelledParams(params, 0).map(p => p.name)).toEqual(['Gain', 'Lead Drive']);
    expect(labelledParams(params, 1).map(p => p.name)).toEqual(['Gain', 'Jumper']);
    expect(labelledParams(params, 2).map(p => p.name)).toEqual(['Gain', 'Cut']);
  });

  it('keeps the parameter ids', () => {
    expect(labelledParams(params, 1).map(p => p.id)).toEqual(['gain', 'switch1']);
  });

  it('passes parameters through for a type beyond the labels', () => {
    expect(labelledParams(params, 7).map(p => p.id)).toEqual(['gain', 'cut', 'switch1', 'control1']);
  });

  it('shows every control in the Ideal view: the model’s names where it has them, generic ones elsewhere', () => {
    expect(labelledParams(params, 0, true).map(p => p.name)).toEqual(['Gain', 'Cut', 'Switch 1', 'Lead Drive']);
    expect(labelledParams(params, 1, true).map(p => p.name)).toEqual(['Gain', 'Cut', 'Jumper', 'Control 1']);
  });
});

describe('per-type defaults', () => {
  const type: ParameterInfo = { ...param('type', 'Type'), kind: 'enum', max: 2, default: 0 };
  const boost: ParameterInfo = { ...param('control1', 'Solo Boost'), typeDefaults: [5, 0, 5] };

  it('gives each type its own default', () => {
    expect(defaultOn(boost, 1)).toBe(0);
    expect(defaultOn(boost, 2)).toBe(5);
    expect(defaultOn(param('gain', 'Gain'), 1)).toBe(5);
  });

  it('carries the type’s default on the labelled parameters, so the knob resets there', () => {
    expect(labelledParams([boost], 1)[0]?.default).toBe(0);
    expect(labelledParams([boost], 0)[0]?.default).toBe(5);
  });

  it('reads an unset value as the default on the block’s type', () => {
    const values: Record<string, number> = { type: 1 };
    expect(blockDefault([type, boost], boost, id => values[id])).toBe(0);
    expect(blockDefault([type, boost], boost, () => undefined)).toBe(5);
  });
});
