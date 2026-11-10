import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import type { Preset } from '@cathode/api-client';
import { EventStream } from '../core/events';
import { LiveStore } from './live.store';

/** The two snapshots, and the load of a preset ({@link LiveState.load}) they were taken on. */
interface Held {
  load: number | undefined;
  active: 'A' | 'B';
  A: Preset | null;
  B: Preset | null;
}

/**
 * Rig events that put other content into the working copy without loading a preset: what the snapshots held is no
 * longer two versions of what is being edited.
 */
const ReplacingEvents = new Set(['presetReverted', 'presetCleared', 'snapshotLoaded']);

/**
 * A/B compare: two snapshots of the working copy. Switching stores the current content in the slot being left and
 * puts the other one live (an undoable replace on the Server), so both versions keep their edits.
 *
 * The snapshots belong to one load of a preset. Once another is loaded, by this editor, another one, a footswitch or
 * a MIDI program change, they are forgotten: switching would otherwise put the previous preset's content into the new
 * one. They are forgotten too when the working copy is reverted, cleared or replaced by a snapshot, from anywhere.
 */
@Injectable({ providedIn: 'root' })
export class CompareStore {
  private readonly live = inject(LiveStore);
  private readonly held = signal<Held>({ load: undefined, active: 'A', A: null, B: null });
  /** What is held, or nothing when it was taken on an earlier load than the one the working copy is on. */
  private readonly slots = computed<Held>(() => {
    const held = this.held();
    const load = this.live.state()?.load;
    return held.load === load ? held : { load, active: 'A', A: null, B: null };
  });

  readonly active = computed(() => this.slots().active);
  /** Whether B has been captured yet; until then, switching to B starts it as a copy of A. */
  readonly hasB = computed(() => this.slots().B !== null);

  constructor() {
    const subscription = inject(EventStream).events$.subscribe(
      event => ReplacingEvents.has(event.type) && this.reset()
    );
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
  }

  async switchTo(slot: 'A' | 'B'): Promise<void> {
    const current = this.live.preset();
    const slots = this.slots();
    if (!current || slot === slots.active) {
      return;
    }

    const target = slots[slot] ?? current;
    const leaving: Held = { ...slots, [slots.active]: structuredClone(current) };
    this.held.set(leaving);
    if (await this.live.replace({ ...structuredClone(target), id: current.id })) {
      // Only if nothing forgot the snapshots while the replace was on its way.
      this.held.update(held => (held === leaving ? { ...held, active: slot } : held));
    }
  }

  /** Forget both snapshots (the working copy now holds something else). */
  reset(): void {
    this.held.set({ load: this.live.state()?.load, active: 'A', A: null, B: null });
  }
}
