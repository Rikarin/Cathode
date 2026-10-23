import type { ParameterInfo } from '@cathode/api-client';

/**
 * A block's parameters as the selected type shows them: where a parameter carries `typeLabels` (the Circuit Amp's
 * per-model panels), it takes that type's name for it, and is left out where the name is empty (the model has no such
 * control). Parameters without `typeLabels` pass through unchanged.
 */
export function labelledParams(params: readonly ParameterInfo[], type: number): ParameterInfo[] {
  const index = Math.round(type);
  return params.flatMap(p => {
    const label = p.typeLabels?.[index];
    if (label === undefined || label === null || label === p.name) {
      return [p];
    }

    return label === '' ? [] : [{ ...p, name: label }];
  });
}
