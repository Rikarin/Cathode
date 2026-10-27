import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { XuiNonIdealStateImports } from '@xui/non-ideal-state';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { CatalogStore } from '../state/catalog.store';
import { GlobalStore } from '../state/global.store';
import { LiveStore } from '../state/live.store';
import { type ResolvedControl, resolveControls, withoutControl } from './perform';
import { PerformControl } from './perform-control';

/**
 * Perform: big controls for playing live. The preset's page travels with the preset; the global page shows in every
 * preset that has its blocks. Add a control from any knob's context menu. A control a modifier drives follows its
 * live value.
 */
@Component({
  selector: 'cth-perform-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PerformControl, XuiNonIdealStateImports, XuiSegmentedControlImports],
  host: { class: 'flex h-full flex-col gap-6 overflow-auto p-6' },
  template: `
    <header class="flex items-center gap-4">
      <h1 class="text-lg font-semibold">Perform</h1>
      <xui-segmented-control
        size="sm"
        aria-label="Page"
        [options]="pages"
        [value]="page()"
        (valueChange)="$event && page.set($event)"
      />
    </header>
    @if (controls().length === 0) {
      <xui-non-ideal-state
        class="flex-1"
        title="Nothing on this page yet"
        description="Right-click a knob on a block's page and choose Add to the Perform page."
      />
    } @else {
      <div class="grid grid-cols-[repeat(auto-fill,minmax(10rem,1fr))] gap-6" data-testid="perform-grid">
        @for (c of controls(); track c.control.block + '/' + c.control.param) {
          <cth-perform-control [control]="c" (remove)="remove(c)" />
        }
      </div>
    }
  `
})
export class PerformPage {
  private readonly live = inject(LiveStore);
  private readonly globals = inject(GlobalStore);
  private readonly catalog = inject(CatalogStore);

  protected readonly pages = [
    { label: 'This preset', value: 'preset' as const },
    { label: 'Global', value: 'global' as const }
  ];
  protected readonly page = signal<'preset' | 'global'>('preset');
  protected readonly controls = computed(() =>
    resolveControls(
      this.page() === 'preset' ? (this.live.preset()?.perform ?? []) : (this.globals.settings()?.perform ?? []),
      this.live.preset(),
      type => this.catalog.info(type)
    )
  );

  protected remove(c: ResolvedControl): void {
    if (this.page() === 'preset') {
      void this.live.setPerform(withoutControl(this.live.preset()?.perform ?? [], c.control));
    } else {
      void this.globals.update(g => (g.perform = withoutControl(g.perform, c.control)));
    }
  }
}
