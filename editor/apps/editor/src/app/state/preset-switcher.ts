import { Injectable, inject, signal } from '@angular/core';
import { CompareStore } from './compare.store';
import { GlobalStore } from './global.store';
import { LiveStore } from './live.store';

/** A preset loaded recently, newest first. */
export interface RecentPreset {
  id: string;
  name: string;
}

const RecentKey = 'cathode.recentPresets';
export const RecentLimit = 8;

/** Puts a preset at the front of the recent list, once, keeping at most {@link RecentLimit}. */
export function pushRecent(list: readonly RecentPreset[], entry: RecentPreset, limit = RecentLimit): RecentPreset[] {
  return [entry, ...list.filter(p => p.id !== entry.id)].slice(0, limit);
}

/** Whether changing preset must ask first: unsaved edits, and the global Confirm Preset Change on (its default). */
export function needsConfirm(dirty: boolean | undefined, confirmSetting: boolean | undefined): boolean {
  return (dirty ?? false) && (confirmSetting ?? true);
}

/**
 * Changing presets from the editor: asks before throwing away unsaved edits (when the global setting says so),
 * resets A/B compare, and keeps the Recent Presets list (per editor, in local storage).
 */
@Injectable({ providedIn: 'root' })
export class PresetSwitcher {
  private readonly live = inject(LiveStore);
  private readonly globals = inject(GlobalStore);
  private readonly compare = inject(CompareStore);

  readonly recent = signal<RecentPreset[]>(readRecent());

  /** Asks the person; replaceable in tests. */
  confirm: (message: string) => boolean = message => window.confirm(message);

  async load(id: string, name: string): Promise<boolean> {
    const current = this.live.preset();
    if (
      needsConfirm(this.live.state()?.dirty, this.globals.settings()?.confirmPresetChange) &&
      !this.confirm(`“${current?.name ?? 'This preset'}” has unsaved changes. Discard them and load “${name}”?`)
    ) {
      return false;
    }

    if (!(await this.live.load(id))) {
      return false;
    }

    this.compare.reset();
    this.remember(id, name);
    return true;
  }

  remember(id: string, name: string): void {
    this.recent.update(list => pushRecent(list, { id, name }));
    try {
      localStorage.setItem(RecentKey, JSON.stringify(this.recent()));
    } catch {
      // Storage can be unavailable (a private window); the list then lasts this session.
    }
  }
}

function readRecent(): RecentPreset[] {
  try {
    const stored = JSON.parse(localStorage.getItem(RecentKey) ?? '[]') as unknown;
    return Array.isArray(stored)
      ? stored.filter((p): p is RecentPreset => typeof p?.id === 'string' && typeof p?.name === 'string')
      : [];
  } catch {
    return [];
  }
}
