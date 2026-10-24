import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import {
  type ChannelName,
  type FootswitchOverride,
  type LiveState,
  type PerformControl,
  type Preset,
  type PresetBlock,
  type PresetModifier,
  type RigEvent,
  type SceneMidiMessage,
  unwrap
} from '@cathode/api-client';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';

interface PendingParams {
  channel: ChannelName;
  values: Record<string, number>;
}

/**
 * The live rig (docs/adr/0004-state-ownership.md). The Server's {@link LiveState} is the only truth: every mutation
 * answers with the new state, and events from other editors (or MIDI) trigger a refetch when they reveal a revision
 * we have not seen.
 *
 * Knob drags are the one optimistic path. The dragged value shows at once from {@link overrides}, and at most one
 * PATCH per block is in flight: values that arrive meanwhile are merged and sent when it returns.
 */
@Injectable({ providedIn: 'root' })
export class LiveStore {
  private readonly connection = inject(ConnectionService);
  private readonly events = inject(EventStream);
  private readonly notify = inject(Notify);
  private refreshing: Promise<void> | null = null;
  private readonly inFlight = new Set<string>();
  private readonly pending = new Map<string, PendingParams>();

  readonly state = signal<LiveState | null>(null);
  readonly preset = computed(() => this.state()?.preset ?? null);
  readonly activeScene = computed(() => this.state()?.activeScene ?? 0);
  readonly selectedId = signal<string | null>(null);
  readonly selected = computed(() => this.preset()?.blocks.find(b => b.id === this.selectedId()) ?? null);

  /** Values being dragged, keyed `block/channel/param`, shown until the Server has confirmed them. */
  readonly overrides = signal<ReadonlyMap<string, number>>(new Map());

  constructor() {
    const subscriptions = [
      this.events.connected$.subscribe(() => void this.refresh()),
      this.events.events$.subscribe(event => this.onEvent(event))
    ];
    inject(DestroyRef).onDestroy(() => subscriptions.forEach(s => s.unsubscribe()));
  }

  /** The channel a block uses in the active scene. */
  channelOf(blockId: string): ChannelName {
    const scene = this.preset()?.scenes[this.activeScene()];
    return (scene?.blocks[blockId]?.channel ?? 'A') as ChannelName;
  }

  isBypassed(blockId: string): boolean {
    const scene = this.preset()?.scenes[this.activeScene()];
    return scene?.blocks[blockId]?.bypassed ?? false;
  }

  /** A parameter's value on a channel: a pending drag, the stored value, or undefined for the type's default. */
  valueOf(block: PresetBlock, channel: ChannelName, param: string): number | undefined {
    return this.overrides().get(`${block.id}/${channel}/${param}`) ?? block.channels[channel]?.[param];
  }

  async refresh(): Promise<void> {
    // Coalesce: a burst of events needs one refetch, not one each.
    this.refreshing ??= (async () => {
      try {
        this.apply(unwrap(await this.connection.client.GET('/api/v1/live')));
      } catch (error) {
        this.notify.error(error);
      } finally {
        this.refreshing = null;
      }
    })();
    return this.refreshing;
  }

  setParam(blockId: string, channel: ChannelName, param: string, value: number): void {
    this.overrides.update(map => new Map(map).set(`${blockId}/${channel}/${param}`, value));
    const key = `${blockId}/${channel}`;
    const pending = this.pending.get(key) ?? { channel, values: {} };
    pending.values[param] = value;
    this.pending.set(key, pending);
    void this.flush(blockId, key);
  }

  newPreset = () => this.mutate(c => c.POST('/api/v1/live/new'));
  load = (id: string) => this.mutate(c => c.PUT('/api/v1/live/preset/{id}', { params: { path: { id } } }));
  replace = (preset: Preset) => this.mutate(c => c.PUT('/api/v1/live', { body: preset }));
  save = (name?: string, asNew = false) =>
    this.mutate(c => c.POST('/api/v1/live/save', { body: { name: name ?? null, asNew } }));
  undo = () => this.mutate(c => c.POST('/api/v1/live/undo'));
  redo = () => this.mutate(c => c.POST('/api/v1/live/redo'));

  addBlock = (type: string, row: number, column: number) =>
    this.mutate(c => c.POST('/api/v1/live/blocks', { body: { type, row, column, id: null } }));
  removeBlock = (id: string) => this.mutate(c => c.DELETE('/api/v1/live/blocks/{id}', { params: { path: { id } } }));
  moveBlock = (id: string, row: number, column: number) =>
    this.mutate(c => c.PUT('/api/v1/live/blocks/{id}/position', { params: { path: { id } }, body: { row, column } }));
  setBypass = (id: string, bypassed: boolean) =>
    this.mutate(c => c.PUT('/api/v1/live/blocks/{id}/bypass', { params: { path: { id } }, body: { bypassed } }));
  setChannel = (id: string, channel: ChannelName) =>
    this.mutate(c => c.PUT('/api/v1/live/blocks/{id}/channel', { params: { path: { id } }, body: { channel } }));
  setAsset = (id: string, slot: string, asset: string) =>
    this.mutate(c =>
      c.PUT('/api/v1/live/blocks/{id}/assets/{slot}', { params: { path: { id, slot } }, body: { asset } })
    );
  addCable = (from: string, to: string) => this.mutate(c => c.POST('/api/v1/live/cables', { body: { from, to } }));
  removeCable = (from: string, to: string) =>
    this.mutate(c => c.DELETE('/api/v1/live/cables', { params: { query: { from, to } } }));
  selectScene = (scene: number) => this.mutate(c => c.PUT('/api/v1/live/scenes/active', { body: { scene } }));
  setBypassMode = (id: string, mode: string) =>
    this.mutate(c => c.PUT('/api/v1/live/blocks/{id}/bypass-mode', { params: { path: { id } }, body: { mode } }));
  setModifiers = (modifiers: PresetModifier[]) =>
    this.mutate(c => c.PUT('/api/v1/live/modifiers', { body: modifiers }));
  setControllers = (values: Record<string, number>) =>
    this.mutate(c => c.PATCH('/api/v1/live/controllers', { body: values }));
  setTempo = (bpm: number) => this.mutate(c => c.PUT('/api/v1/live/tempo', { body: { bpm } }));
  updateScene = (scene: number, change: { level?: number; controllers?: number[]; midi?: SceneMidiMessage[] }) =>
    this.mutate(c =>
      c.PATCH('/api/v1/live/scenes/{scene}', {
        params: { path: { scene } },
        body: {
          name: null,
          level: change.level ?? null,
          controllers: change.controllers ?? null,
          midi: change.midi ?? null
        }
      })
    );
  copyChannel = (id: string, from: ChannelName, to: ChannelName) =>
    this.mutate(c =>
      c.POST('/api/v1/live/blocks/{id}/channels/copy', { params: { path: { id } }, body: { from, to } })
    );
  makeGlobal = (id: string, name: string) =>
    this.mutate(c => c.POST('/api/v1/live/blocks/{id}/global', { params: { path: { id } }, body: { name } }));
  useGlobal = (id: string, globalId: string) =>
    this.mutate(c => c.PUT('/api/v1/live/blocks/{id}/global/{globalId}', { params: { path: { id, globalId } } }));
  unlinkGlobal = (id: string) =>
    this.mutate(c => c.DELETE('/api/v1/live/blocks/{id}/global', { params: { path: { id } } }));

  // The editor workflow (docs/02-architecture.md §7.5); each is one undo step on the Server.
  revert = () => this.mutate(c => c.POST('/api/v1/live/revert'));
  clear = () => this.mutate(c => c.POST('/api/v1/live/clear'));
  loadSnapshot = (id: string) =>
    this.mutate(c => c.POST('/api/v1/live/snapshots/{id}/load', { params: { path: { id } } }));
  newFromTemplate = (id: string) =>
    this.mutate(c => c.POST('/api/v1/live/templates/{id}/new', { params: { path: { id } } }));
  replaceBlock = (id: string, type: string) =>
    this.mutate(c => c.PUT('/api/v1/live/blocks/{id}/type', { params: { path: { id } }, body: { type } }));
  dropBlock = (id: string, target: string, mode: 'swap' | 'replace') =>
    this.mutate(c => c.POST('/api/v1/live/blocks/{id}/drop', { params: { path: { id } }, body: { target, mode } }));
  moveLine = (axis: 'row' | 'column', from: number, to: number) =>
    this.mutate(c => c.POST('/api/v1/live/grid/move', { body: { axis, from, to } }));
  disconnect = (id: string) =>
    this.mutate(c => c.DELETE('/api/v1/live/blocks/{id}/cables', { params: { path: { id } } }));
  pasteSettings = (id: string, type: string, channels: PresetBlock['channels'], assets?: PresetBlock['assets']) =>
    this.mutate(c =>
      c.PUT('/api/v1/live/blocks/{id}/settings', {
        params: { path: { id } },
        body: { type, channels, assets: assets ?? null }
      })
    );
  setAllScenes = (id: string, change: { bypassed?: boolean; channel?: ChannelName }) =>
    this.mutate(c =>
      c.PUT('/api/v1/live/blocks/{id}/all-scenes', {
        params: { path: { id } },
        body: { bypassed: change.bypassed ?? null, channel: change.channel ?? null }
      })
    );
  setPerform = (controls: PerformControl[]) => this.mutate(c => c.PUT('/api/v1/live/perform', { body: controls }));
  setFootswitchOverrides = (overrides: FootswitchOverride[]) =>
    this.mutate(c => c.PUT('/api/v1/live/footswitch-overrides', { body: overrides }));

  /** The modifier on a parameter, if any. */
  modifierOf(block: string, param: string): PresetModifier | undefined {
    return this.preset()?.modifiers.find(m => m.block === block && m.param === param);
  }

  /** Adds, replaces (same parameter) or, with null, removes one modifier. */
  putModifier(block: string, param: string, modifier: PresetModifier | null): Promise<boolean> {
    const others = (this.preset()?.modifiers ?? []).filter(m => !(m.block === block && m.param === param));
    return this.setModifiers(modifier ? [...others, modifier] : others);
  }

  async tap(): Promise<void> {
    await this.connection.client.POST('/api/v1/live/tap');
  }

  renameScene = (scene: number, name: string) =>
    this.mutate(c =>
      c.PATCH('/api/v1/live/scenes/{scene}', {
        params: { path: { scene } },
        body: { name, level: null, controllers: null, midi: null }
      })
    );

  /** Runs a mutation, adopts the state it answers with, and reports a failure. Resolves to whether it worked. */
  async mutate(
    call: (client: ConnectionService['client']) => Promise<{ data?: LiveState; error?: unknown; response: Response }>
  ): Promise<boolean> {
    try {
      this.apply(unwrap(await call(this.connection.client)));
      return true;
    } catch (error) {
      this.notify.error(error);
      return false;
    }
  }

  private apply(state: LiveState): void {
    const current = this.state();
    if (current && state.revision < current.revision) {
      return; // an older answer that overtook a newer one
    }

    this.state.set(state);
    if (this.selectedId() && !state.preset.blocks.some(b => b.id === this.selectedId())) {
      this.selectedId.set(null);
    }
  }

  private onEvent(event: RigEvent): void {
    const current = this.state();
    if (!current || event.revision > current.revision) {
      void this.refresh();
    }
  }

  private async flush(blockId: string, key: string): Promise<void> {
    if (this.inFlight.has(key)) {
      return; // sent when the request in flight returns
    }

    const batch = this.pending.get(key);
    if (!batch) {
      return;
    }

    this.pending.delete(key);
    this.inFlight.add(key);
    try {
      await this.mutate(c =>
        c.PATCH('/api/v1/live/blocks/{id}/params', {
          params: { path: { id: blockId }, query: { channel: batch.channel } },
          body: batch.values
        })
      );
    } finally {
      this.inFlight.delete(key);
    }

    if (this.pending.has(key)) {
      await this.flush(blockId, key);
      return;
    }

    // Everything for this block and channel is confirmed: drop its overrides so the stored values show.
    this.overrides.update(map => new Map([...map].filter(([name]) => !name.startsWith(`${key}/`))));
  }
}
