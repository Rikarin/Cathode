import { Injectable, InjectionToken, computed, effect, inject, signal } from '@angular/core';
import type { KnobPreferences, MeterPreferences } from '@cathode/audio-ui';

/** How the user likes the editor: kept per user in this browser's (or the desktop app's) local storage. */
export interface Preferences {
  /** Linear: drag up and down. Circular: drag around the knob, as you would turn it. */
  knobMode: 'linear' | 'circular';
  /** What the mouse wheel does over a knob. */
  wheel: 'normal' | 'reverse' | 'off';
  tooltips: boolean;
  /** Meter redraws per second; 0 stops them. */
  meterRate: number;
  /** The Circuit Amp page: Authentic shows only the model's own controls, Ideal shows every control. */
  ampView: 'authentic' | 'ideal';
}

export const DefaultPreferences: Preferences = {
  knobMode: 'linear',
  wheel: 'normal',
  tooltips: true,
  meterRate: 60,
  ampView: 'authentic'
};

export const MeterRates = [60, 30, 15, 0] as const;

const Key = 'cathode.preferences';

/** Where preferences are kept; local storage by default, replaced in tests. */
export const PreferencesStorage = new InjectionToken<Pick<Storage, 'getItem' | 'setItem'> | null>(
  'PreferencesStorage',
  {
    providedIn: 'root',
    factory: () => {
      try {
        return typeof localStorage === 'undefined' ? null : localStorage;
      } catch {
        return null; // storage blocked: preferences last for the session
      }
    }
  }
);

/** Reads stored preferences, keeping only known values so a damaged or older entry falls back to the defaults. */
export function parsePreferences(text: string | null): Preferences {
  let stored: Partial<Record<keyof Preferences, unknown>> = {};
  try {
    stored = text ? (JSON.parse(text) ?? {}) : {};
  } catch {
    stored = {};
  }

  const pick = <K extends keyof Preferences>(key: K, allowed: readonly Preferences[K][]): Preferences[K] =>
    allowed.includes(stored[key] as Preferences[K]) ? (stored[key] as Preferences[K]) : DefaultPreferences[key];
  return {
    knobMode: pick('knobMode', ['linear', 'circular']),
    wheel: pick('wheel', ['normal', 'reverse', 'off']),
    tooltips: pick('tooltips', [true, false]),
    meterRate: pick('meterRate', [...MeterRates]),
    ampView: pick('ampView', ['authentic', 'ideal'])
  };
}

@Injectable({ providedIn: 'root' })
export class PreferencesStore {
  private readonly storage = inject(PreferencesStorage);

  readonly value = signal<Preferences>(parsePreferences(this.read()));
  readonly knobMode = computed(() => this.value().knobMode);
  readonly wheel = computed(() => this.value().wheel);
  readonly tooltips = computed(() => this.value().tooltips);
  readonly meterRate = computed(() => this.value().meterRate);
  readonly ampView = computed(() => this.value().ampView);

  constructor() {
    // Tooltips are xUI overlays all over the app: one class on the root hides them (styles.css).
    effect(() => document.documentElement.classList.toggle('cth-no-tooltips', !this.tooltips()));
  }

  set<K extends keyof Preferences>(key: K, value: Preferences[K]): void {
    this.value.update(current => ({ ...current, [key]: value }));
    try {
      this.storage?.setItem(Key, JSON.stringify(this.value()));
    } catch {
      // full or blocked: the change still applies until the window closes
    }
  }

  private read(): string | null {
    try {
      return this.storage?.getItem(Key) ?? null;
    } catch {
      return null;
    }
  }
}

/** The knob and meter settings, for audio-ui's injection tokens. */
export function knobPreferences(store: PreferencesStore): KnobPreferences {
  return { mode: store.knobMode, wheel: store.wheel, tooltips: store.tooltips };
}

export function meterPreferences(store: PreferencesStore): MeterPreferences {
  return { rate: store.meterRate };
}
