// The shape of engine-params.generated.ts, which `pnpm manual` (tools/manual.cs) writes from the engine's block
// registry.

/** One parameter of a block, as the engine describes it. */
export interface EngineParam {
  /** Stable id, as presets and the API use it; the knob's anchor on its page is `#knob-<id>`. */
  readonly id: string;
  /** The name the editor shows. */
  readonly name: string;
  /** A continuous knob, a choice from `options`, or an on/off switch. */
  readonly kind: 'float' | 'choice' | 'switch';
  readonly min: number;
  readonly max: number;
  /** The default value; for a choice, the index of the default option. */
  readonly default: number;
  readonly unit?: string;
  /** A knob whose travel is logarithmic (frequencies, times); absent for a linear one. */
  readonly taper?: 'log';
  /** The editor page the knob sits on; absent for a block shown on one page. */
  readonly page?: string;
  readonly options?: readonly string[];
  /**
   * For a block whose models differ in their front panels (the Circuit Amp): the knob's name on each value of the
   * block's `type` parameter, by index; an empty name means that model has no such control.
   */
  readonly typeLabels?: readonly string[];
  /**
   * Where a model ships the knob at another setting than the rest (the Quick Rod's Solo Boost, off): its default on each
   * value of the block's `type` parameter, by index, in place of `default`.
   */
  readonly typeDefaults?: readonly number[];
  /**
   * For a knob that means something only with another knob (a choice or a switch of the block) at some settings: that
   * knob's id and the values it is shown at; the app hides it otherwise.
   */
  readonly visibleWhen?: { readonly param: string; readonly values: readonly number[] };
}

/** One block type, as the engine registers it. */
export interface EngineBlock {
  readonly typeId: string;
  readonly name: string;
  readonly category: string;
  /** Channels (A–D) the block offers. */
  readonly channels: number;
  /** How many of it one preset may hold. */
  readonly maxInstances: number;
  /** The editor's pages in order; absent for a block shown on one page. */
  readonly pages?: readonly string[];
  /** The parameters set back to their defaults when the block's type changes. */
  readonly typeResets?: readonly string[];
  /** Every parameter, in the engine's order. */
  readonly params: readonly EngineParam[];
}
