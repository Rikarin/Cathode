import { Injectable, computed, inject, signal } from '@angular/core';
import {
  BuiltInTypes,
  type ChannelName,
  ChannelNames,
  type ParameterInfo,
  type PresetBlock
} from '@cathode/api-client';
import { ShortcutService } from '../shell/shortcuts';
import { CatalogStore } from './catalog.store';
import { LiveStore } from './live.store';

/** A parameter a dialog acts on (Parameter Batch Set). */
export interface ParamTarget {
  block: PresetBlock;
  param: ParameterInfo;
  channel: ChannelName;
  value: number;
}

export function isBuiltIn(type: string): boolean {
  return type === BuiltInTypes.input || type === BuiltInTypes.output || type === BuiltInTypes.shunt;
}

/** The next channel of a block with `count` channels: A → B → … → A. */
export function nextChannel(current: ChannelName, count: number): ChannelName {
  const index = ChannelNames.indexOf(current);
  return ChannelNames[(index + 1) % Math.max(1, Math.min(count, ChannelNames.length))];
}

/**
 * Shell state the shortcuts, menus and dialogs share (which dialog is open, Block Info), and the commands on the
 * selected block that more than one place offers (the grid's menu, the keyboard).
 */
@Injectable({ providedIn: 'root' })
export class UiStore {
  private readonly live = inject(LiveStore);
  private readonly catalog = inject(CatalogStore);
  private readonly shortcuts = inject(ShortcutService);

  readonly presetPicker = signal<'browse' | 'goto' | null>(null);
  readonly tunerOpen = signal(false);
  readonly preferencesOpen = signal(false);
  readonly saveBlockOpen = signal(false);
  readonly batchSet = signal<ParamTarget | null>(null);
  /** Tiles show their abbreviation, channel letter and a Global mark: F1 toggles, ⌥ shows while held. */
  readonly blockInfo = computed(() => this.shortcuts.blockInfo() || this.shortcuts.altHeld());

  /** The selected block, unless it is a shunt (which has nothing to act on). */
  readonly block = computed(() => {
    const block = this.live.selected();
    return block && block.type !== BuiltInTypes.shunt ? block : null;
  });

  channels(block: PresetBlock): number {
    return this.catalog.info(block.type)?.channels ?? 0;
  }

  toggleBypass(block = this.block()): void {
    if (block && !isBuiltIn(block.type)) {
      void this.live.setBypass(block.id, !this.live.isBypassed(block.id));
    }
  }

  setChannel(channel: ChannelName, block = this.block()): void {
    if (block && ChannelNames.indexOf(channel) < this.channels(block)) {
      void this.live.setChannel(block.id, channel);
    }
  }

  nextChannel(block = this.block()): void {
    if (block && this.channels(block) > 1) {
      void this.live.setChannel(block.id, nextChannel(this.live.channelOf(block.id), this.channels(block)));
    }
  }

  allScenes(change: { bypassed?: boolean; channel?: ChannelName }, block = this.block()): void {
    if (block && (change.channel !== undefined || !isBuiltIn(block.type))) {
      void this.live.setAllScenes(block.id, change);
    }
  }

  toggleGlobal(block = this.block()): void {
    if (!block || isBuiltIn(block.type)) {
      return;
    }

    const name = `${this.catalog.info(block.type)?.name ?? block.type} (${block.id})`;
    void (block.global ? this.live.unlinkGlobal(block.id) : this.live.makeGlobal(block.id, name));
  }
}
