import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { XuiNonIdealStateImports } from '@xui/non-ideal-state';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { CatalogStore } from '../state/catalog.store';
import { GlobalStore } from '../state/global.store';
import { LiveStore } from '../state/live.store';
import type { PerformControl as Control } from '@cathode/api-client';
import { type ResolvedControl, movedControl, resolveControls, withLabel, withoutControl } from './perform';
import { PerformControl, PerformDragType } from './perform-control';
import { SetlistBar } from './setlist-bar';

/**
 * Perform: big controls for playing live, under the setlist being played (its current and next song). The preset's
 * page travels with the preset; the global page shows in every preset that has its blocks. Add a control from any
 * knob's context menu. A control a modifier drives follows its live value. Controls can be renamed, and reordered by
 * their move buttons or by dragging their grip onto another.
 */
@Component({
  selector: 'cth-perform-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PerformControl, SetlistBar, XuiNonIdealStateImports, XuiSegmentedControlImports],
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
    <cth-setlist-bar />
    @if (controls().length === 0) {
      <xui-non-ideal-state
        class="flex-1"
        title="Nothing on this page yet"
        description="Right-click a knob on a block's page and choose Add to the Perform page."
      />
    } @else {
      <div class="grid grid-cols-[repeat(auto-fill,minmax(10rem,1fr))] gap-6" data-testid="perform-grid">
        @for (c of controls(); track c.control.block + '/' + c.control.param; let i = $index, first = $first, last = $last) {
          <cth-perform-control
            [control]="c"
            [first]="first"
            [last]="last"
            [class.ring-2]="dropTarget() === i && dragging() !== i"
            [class.ring-primary]="dropTarget() === i && dragging() !== i"
            (remove)="remove(c)"
            (rename)="rename(c, $event)"
            (move)="move(i, i + $event)"
            (dragstart)="dragging.set(i)"
            (dragend)="dragging.set(null); dropTarget.set(null)"
            (dragover)="dragOver($event, i)"
            (dragleave)="dropTarget() === i && dropTarget.set(null)"
            (drop)="drop($event, i)"
          />
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

  /** The index of the control being dragged by its grip, and of the one it is over. */
  protected readonly dragging = signal<number | null>(null);
  protected readonly dropTarget = signal<number | null>(null);

  protected remove(c: ResolvedControl): void {
    this.change(page => withoutControl(page, c.control));
  }

  protected rename(c: ResolvedControl, label: string | null): void {
    this.change(page => withLabel(page, c.control, label));
  }

  protected move(from: number, to: number): void {
    if (to >= 0 && to < this.controls().length && from !== to) {
      this.change(page => movedControl(page, from, to));
    }
  }

  protected dragOver(event: DragEvent, index: number): void {
    if (this.dragging() !== null && event.dataTransfer?.types.includes(PerformDragType)) {
      event.preventDefault(); // a drop target
      event.dataTransfer.dropEffect = 'move';
      this.dropTarget.set(index);
    }
  }

  protected drop(event: DragEvent, index: number): void {
    const from = this.dragging();
    this.dragging.set(null);
    this.dropTarget.set(null);
    if (from !== null) {
      event.preventDefault();
      this.move(from, index);
    }
  }

  /** Saves the page shown, through the preset or the global settings. */
  private change(edit: (page: readonly Control[]) => Control[]): void {
    if (this.page() === 'preset') {
      void this.live.setPerform(edit(this.live.preset()?.perform ?? []));
    } else {
      void this.globals.update(g => (g.perform = edit(g.perform)));
    }
  }
}
