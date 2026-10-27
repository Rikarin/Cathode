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

  /** The name of the preset being loaded, while it loads (the editor shows it over the grid). */
  readonly loading = signal<string | null>(null);

  /** Whether the current preset may be replaced: asks first when it has unsaved changes and the setting says so. */
  mayReplace(name: string): boolean {
    return this.mayDiscard(`load “${name}”`);
  }

  /**
   * Whether the working copy may be thrown away to `then` ("start a new preset"): asks first, as loading does, when it
   * has unsaved changes and the global setting says so.
   */
  mayDiscard(then: string): boolean {
    const current = this.live.preset();
    return (
      !needsConfirm(this.live.state()?.dirty, this.globals.settings()?.confirmPresetChange) ||
      this.confirm(`“${current?.name || 'This preset'}” has unsaved changes. Discard them and ${then}?`)
    );
  }

  /** Starts an empty preset, asking first about unsaved changes. */
  async newPreset(): Promise<boolean> {
    if (!this.mayDiscard('start a new preset') || !(await this.live.newPreset())) {
      return false;
    }

    this.compare.reset();
    return true;
  }

  /** Loads a preset, asking first about unsaved changes unless the caller already has (`confirmed`). */
  async load(id: string, name: string, confirmed = false): Promise<boolean> {
    if (!confirmed && !this.mayReplace(name)) {
      return false;
    }

    this.loading.set(name);
    try {
      if (!(await this.live.load(id))) {
        return false;
      }
    } finally {
      this.loading.set(null);
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
      ? (stored as unknown[]).filter((p): p is RecentPreset => {
          const fields = p as Partial<Record<keyof RecentPreset, unknown>> | null;
          return typeof fields?.id === 'string' && typeof fields.name === 'string';
        })
      : [];
  } catch {
    return [];
  }
}
