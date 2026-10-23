import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { BlockIcons, blockIconName } from './block-icons';

/** How a block category looks: an OKLCH hue and chroma for its gradient. */
export interface BlockCategoryStyle {
  hue: number;
  chroma: number;
}

/**
 * The known categories, in signal-chain order (the block picker lists them so). The hues walk the colour wheel along
 * the chain, so neighbours differ; routing, utility and I/O are muted because they do not colour the sound.
 */
export const BlockCategories: Readonly<Record<string, BlockCategoryStyle>> = {
  Dynamics: { hue: 150, chroma: 0.13 },
  Drive: { hue: 50, chroma: 0.17 },
  Amp: { hue: 25, chroma: 0.17 },
  Cab: { hue: 80, chroma: 0.12 },
  Filter: { hue: 180, chroma: 0.12 },
  EQ: { hue: 205, chroma: 0.12 },
  Pitch: { hue: 355, chroma: 0.15 },
  Modulation: { hue: 325, chroma: 0.15 },
  Delay: { hue: 245, chroma: 0.15 },
  Reverb: { hue: 285, chroma: 0.15 },
  Synth: { hue: 305, chroma: 0.15 },
  Routing: { hue: 230, chroma: 0.05 },
  Utility: { hue: 260, chroma: 0.03 },
  'I/O': { hue: 260, chroma: 0.02 }
};

/** A category's style; one the editor does not know yet gets a stable hue from its name. */
export function blockCategoryStyle(category: string): BlockCategoryStyle {
  const known = BlockCategories[category];
  if (known) {
    return known;
  }

  let hash = 0;
  for (const c of category) {
    hash = (hash * 31 + c.charCodeAt(0)) | 0;
  }

  return { hue: Math.abs(hash) % 360, chroma: 0.13 };
}

/** A CSS gradient for a category: lighter at the top, dark enough under the label for white text. */
export function blockGradient(category: string): string {
  const { hue, chroma } = blockCategoryStyle(category);
  return `linear-gradient(165deg, oklch(0.6 ${chroma} ${hue}), oklch(0.4 ${chroma * 0.8} ${hue}))`;
}

/** The swatch colour for a category (its gradient's middle), for dots and accents. */
export function blockColor(category: string): string {
  const { hue, chroma } = blockCategoryStyle(category);
  return `oklch(0.55 ${chroma} ${hue})`;
}

/**
 * A brighter tone of the category's colour for marks on a dark ground, like a knob's arc; `null` for the muted
 * categories (routing, utility), which have too little colour to carry one.
 */
export function blockAccent(category: string): string | null {
  const { hue, chroma } = blockCategoryStyle(category);
  return chroma < 0.08 ? null : `oklch(0.72 ${chroma} ${hue})`;
}

/**
 * A block as a coloured tile: the category's gradient, its icon near the top and the block's name below. Used by the
 * grid and the block picker so the colours mean the same everywhere. Badges go in the default slot, positioned
 * absolutely by the caller.
 */
@Component({
  selector: 'cth-block-tile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon],
  providers: [provideIcons(BlockIcons)],
  host: {
    class:
      'relative flex flex-col items-center overflow-hidden rounded-lg px-1.5 text-white transition-[opacity,filter] duration-fast',
    '[class.pt-2]': 'size() === "md"',
    '[class.pt-3]': 'size() === "lg"',
    '[class.opacity-45]': 'bypassed()',
    '[class.grayscale]': 'bypassed()',
    '[style.background]': 'gradient()',
    '[style.box-shadow]':
      '"inset 0 1px 0 rgb(255 255 255 / 0.2), inset 0 0 0 1px rgb(255 255 255 / 0.08), 0 1px 3px rgb(0 0 0 / 0.3)"'
  },
  template: `
    <ng-icon
      class="shrink-0 drop-shadow-sm"
      aria-hidden="true"
      [name]="iconName()"
      [size]="size() === 'lg' ? '36' : '22'"
    />
    <span
      class="w-full truncate text-center leading-tight font-semibold [text-shadow:0_1px_2px_rgb(0_0_0/0.35)]"
      [class]="size() === 'lg' ? 'mt-2 text-[13px]' : 'mt-1 text-xs'"
    >
      {{ name() }}
    </span>
    <ng-content />
  `
})
export class CthBlockTile {
  readonly name = input.required<string>();
  readonly category = input.required<string>();
  /** The block type, for its own icon; without one (or for a type with no drawing yet) the category's is used. */
  readonly typeId = input<string | null>(null);
  readonly bypassed = input(false);
  /** `md` for the grid's cells, `lg` for the block picker. */
  readonly size = input<'md' | 'lg'>('md');

  protected readonly gradient = computed(() => blockGradient(this.category()));
  protected readonly iconName = computed(() => blockIconName(this.typeId(), this.category()));
}
