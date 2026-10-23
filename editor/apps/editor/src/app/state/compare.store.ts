import { Injectable, computed, inject, signal } from '@angular/core';
import type { Preset } from '@cathode/api-client';
import { LiveStore } from './live.store';

/**
 * A/B compare: two snapshots of the working copy. Switching stores the current content in the slot being left and
 * puts the other one live (an undoable replace on the Server), so both versions keep their edits.
 */
@Injectable({ providedIn: 'root' })
export class CompareStore {
  private readonly live = inject(LiveStore);
  private readonly slots = signal<{ A: Preset | null; B: Preset | null }>({ A: null, B: null });

  readonly active = signal<'A' | 'B'>('A');
  /** Whether B has been captured yet; until then, switching to B starts it as a copy of A. */
  readonly hasB = computed(() => this.slots().B !== null);

  async switchTo(slot: 'A' | 'B'): Promise<void> {
    const current = this.live.preset();
    if (!current || slot === this.active()) {
      return;
    }

    const leaving = this.active();
    const target = this.slots()[slot] ?? current;
    this.slots.update(slots => ({ ...slots, [leaving]: structuredClone(current) }));
    if (await this.live.replace({ ...structuredClone(target), id: current.id })) {
      this.active.set(slot);
    }
  }

  /** Forget both snapshots (after loading another preset). */
  reset(): void {
    this.slots.set({ A: null, B: null });
    this.active.set('A');
  }
}
