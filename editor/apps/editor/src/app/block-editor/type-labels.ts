import type { ParameterInfo } from '@cathode/api-client';

/**
 * The parameter's default on type `type` (the value of its block's `type` parameter): its `typeDefaults` entry for
 * that type, where a model ships the control at another setting (the Quick Rod's Solo Boost, off), else its `default`.
 * A value a channel does not store is this.
 */
export function defaultOn(param: ParameterInfo, type: number): number {
  return param.typeDefaults?.[Math.round(type)] ?? param.default;
}

/**
 * The default of `param` in a block whose values `valueOf` reads (stored or pending; undefined where unset): its
 * default on the block's type, the `type` value or, unset, the type parameter's own default.
 */
export function blockDefault(
  params: readonly ParameterInfo[],
  param: ParameterInfo,
  valueOf: (id: string) => number | undefined
): number {
  if (!param.typeDefaults) {
    return param.default;
  }

  const typeParam = params.find(p => p.id === 'type');
  return defaultOn(param, valueOf('type') ?? typeParam?.default ?? 0);
}

/**
 * A block's parameters as the selected type shows them: where a parameter carries `typeLabels` (the Circuit Amp's
 * per-model panels), it takes that type's name for it, and is left out where the name is empty (the model has no such
 * control). Parameters without `typeLabels` pass through unchanged. Each carries its default on that type
 * (`typeDefaults`), so its knob resets there and an unset value reads as it.
 *
 * `ideal` is the Circuit Amp's Ideal view: every control shows, under the model's name where it has one and the
 * generic name where it has none. The default, Authentic, is the model's own panel.
 */
export function labelledParams(params: readonly ParameterInfo[], type: number, ideal = false): ParameterInfo[] {
  const index = Math.round(type);
  return params.flatMap(p => {
    const typed = p.typeDefaults ? { ...p, default: defaultOn(p, index) } : p;
    const label = p.typeLabels?.[index];
    if (label === undefined || label === p.name) {
      return [typed];
    }

    if (label === '') {
      return ideal ? [typed] : [];
    }

    return [{ ...typed, name: label }];
  });
}
