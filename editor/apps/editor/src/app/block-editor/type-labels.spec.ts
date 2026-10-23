import type { ParameterInfo } from '@cathode/api-client';
import { labelledParams } from './type-labels';

function param(id: string, name: string, typeLabels?: string[]): ParameterInfo {
  return { id, name, kind: 'float', min: 0, max: 10, default: 5, unit: '', taper: 'linear', typeLabels };
}

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
});
