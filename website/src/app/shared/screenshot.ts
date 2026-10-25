import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { matImageRound } from '@ng-icons/material-icons/round';
import type { Shot } from '../screenshots';

/**
 * A screenshot of the app. A crop of part of the app sits in a glass frame with a window bar; a whole window, which
 * has its own, is shown as it is. Until its file is in `public/screenshots` (see `screenshots.ts`), the frame shows what
 * belongs there instead, at the same size, so the layout does not move when the picture arrives.
 */
@Component({
  selector: 'cw-screenshot',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon],
  providers: [provideIcons({ matImageRound })],
  host: { '[class]': 'hostClass()' },
  template: `
    @if (bare()) {
      <img
        class="block w-full"
        [src]="shot().src"
        [alt]="shot().alt"
        [style.aspect-ratio]="shot().aspect"
        [attr.loading]="eager() ? 'eager' : 'lazy'"
        [attr.fetchpriority]="eager() ? 'high' : null"
        decoding="async"
      />
    } @else {
      <div class="flex h-9 items-center gap-1.5 border-b border-white/[0.06] px-4" aria-hidden="true">
        <span class="size-2.5 rounded-full bg-white/15"></span>
        <span class="size-2.5 rounded-full bg-white/15"></span>
        <span class="size-2.5 rounded-full bg-white/15"></span>
        <span class="text-foreground-subtle ms-3 truncate text-xs">{{ shot().title }}</span>
      </div>

      @if (shot().src; as src) {
        <img
          class="block w-full bg-black/40"
          [src]="src"
          [alt]="shot().alt"
          [style.aspect-ratio]="shot().aspect"
          [attr.loading]="eager() ? 'eager' : 'lazy'"
          decoding="async"
        />
      } @else {
        <div
          class="relative flex w-full flex-col items-center justify-center gap-3 bg-black/40 p-6 text-center"
          [style.aspect-ratio]="shot().aspect"
          role="img"
          [attr.aria-label]="'Screenshot to come: ' + shot().alt"
        >
          <div class="dots pointer-events-none absolute inset-0" aria-hidden="true"></div>
          <ng-icon name="matImageRound" size="2rem" class="text-signal relative" />
          <p class="text-foreground relative text-sm font-semibold">{{ shot().title }}</p>
          <p class="text-foreground-muted relative max-w-sm text-sm">{{ shot().alt }}</p>
          <code class="text-foreground-subtle relative rounded-md bg-white/5 px-2 py-1 font-mono text-xs"
            >public/screenshots/{{ shot().file }}</code
          >
        </div>
      }
    }
  `
})
export class Screenshot {
  readonly shot = input.required<Shot>();

  /** The hero's picture is in the first viewport; the rest load as they scroll in. */
  readonly eager = input(false);

  protected readonly bare = computed(() => !!this.shot().window && !!this.shot().src);

  protected readonly hostClass = computed(() => (this.bare() ? 'block' : 'card block overflow-hidden'));
}
