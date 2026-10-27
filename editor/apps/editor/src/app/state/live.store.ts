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
  type SceneBlockState,
  type SceneMidiMessage,
  ApiError,
  unwrap
} from '@cathode/api-client';
import { ConnectionService } from '../core/connection';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';

interface PendingParams {
  channel: ChannelName;
  values: Record<string, number>;
  /** The preset the values were dragged on: if another is loaded before they are sent, they are dropped. */
  presetId: string | undefined;
}

type MutationResult = Promise<{ data?: LiveState; error?: unknown; response: Response }>;

/** How often a list replacement is tried again after 412 when the list it was made from has not changed. */
const ListRetries = 3;

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
  /** A refresh was asked for while one was on the wire: read once more when it returns. */
  private again = false;
  /** The newest revision an event has announced. */
  private heard = 0;
  /**
   * The event stream (re)connected: the Server may have restarted, and its revision with it (an in-memory counter
   * that starts at 0). The next state read is taken whatever its revision, as the start of a new count.
   */
  private resync = false;
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
      this.events.connected$.subscribe(() => this.reconnected()),
      this.events.events$.subscribe(event => this.onEvent(event))
    ];
    inject(DestroyRef).onDestroy(() => subscriptions.forEach(s => s.unsubscribe()));
  }

  /** What a block is running: the state Scene Ignore held it in, or the active scene's. */
  liveState(blockId: string): SceneBlockState {
    const state = this.state();
    const held = state?.held?.[blockId];
    return held ?? state?.preset.scenes[state.activeScene]?.blocks[blockId] ?? { bypassed: false, channel: 'A' };
  }

  /** The channel a block is running (the active scene's, unless Scene Ignore held it). */
  channelOf(blockId: string): ChannelName {
    return this.liveState(blockId).channel as ChannelName;
  }

  isBypassed(blockId: string): boolean {
    return this.liveState(blockId).bypassed;
  }

  /** Whether Scene Ignore is on for a channel of a block. */
  ignoresScenes(block: PresetBlock, channel: ChannelName): boolean {
    return block.sceneIgnore?.includes(channel) ?? false;
  }

  /** A parameter's value on a channel: a pending drag, the stored value, or undefined for the type's default. */
  valueOf(block: PresetBlock, channel: ChannelName, param: string): number | undefined {
    return this.overrides().get(`${block.id}/${channel}/${param}`) ?? block.channels[channel]?.[param];
  }

  /**
   * Reads the live state. Coalesced: a burst of events needs one refetch, not one each. A call while a read is on the
   * wire queues one more read after it (however many calls come), and so does an event for a revision newer than the
   * read brings back, so the store never settles on a state older than the last change it heard of.
   */
  refresh(): Promise<void> {
    this.again = true;
    this.refreshing ??= this.catchUp().finally(() => (this.refreshing = null));
    return this.refreshing;
  }

  setParam(blockId: string, channel: ChannelName, param: string, value: number): void {
    this.overrides.update(map => new Map(map).set(`${blockId}/${channel}/${param}`, value));
    const key = `${blockId}/${channel}`;
    const presetId = this.preset()?.id;
    const queued = this.pending.get(key);
    const pending = queued && queued.presetId === presetId ? queued : { channel, values: {}, presetId };
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
  /** Replaces every modifier; refused, not applied, if another client changed them since (see {@link replaceList}). */
  setModifiers = (modifiers: PresetModifier[]) =>
    this.replaceList(
      p => p.modifiers,
      (c, headers) => c.PUT('/api/v1/live/modifiers', { body: modifiers, headers })
    );
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
  /** Mutes or unmutes every output: live only, never saved. */
  setMuted = (muted: boolean) => this.mutate(c => c.PUT('/api/v1/live/mute', { body: { muted } }));
  /** Deletes a global block; blocks linked to it keep its settings as their own. */
  deleteGlobal = (globalId: string) =>
    this.mutate(c => c.DELETE('/api/v1/live/globals/{globalId}', { params: { path: { globalId } } }));
  setSceneIgnore = (id: string, channel: ChannelName, enabled: boolean) =>
    this.mutate(c =>
      c.PUT('/api/v1/live/blocks/{id}/channels/{channel}/scene-ignore', {
        params: { path: { id, channel } },
        body: { enabled }
      })
    );
  swapChannels = (id: string, from: ChannelName, to: ChannelName) =>
    this.mutate(c =>
      c.POST('/api/v1/live/blocks/{id}/channels/swap', { params: { path: { id } }, body: { from, to } })
    );
  resetChannel = (id: string, channel: ChannelName) =>
    this.mutate(c =>
      c.POST('/api/v1/live/blocks/{id}/channels/{channel}/reset', { params: { path: { id, channel } } })
    );
  resetBlock = (id: string) => this.mutate(c => c.POST('/api/v1/live/blocks/{id}/reset', { params: { path: { id } } }));
  setSceneBlock = (scene: number, id: string, state: SceneBlockState) =>
    this.mutate(c =>
      c.PUT('/api/v1/live/scenes/{scene}/blocks/{id}', { params: { path: { scene, id } }, body: state })
    );
  copyScene = (from: number, to: number) =>
    this.mutate(c => c.POST('/api/v1/live/scenes/copy', { body: { from, to } }));
  swapScenes = (from: number, to: number) =>
    this.mutate(c => c.POST('/api/v1/live/scenes/swap', { body: { from, to } }));

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
  setPerform = (controls: PerformControl[]) =>
    this.replaceList(
      p => p.perform,
      (c, headers) => c.PUT('/api/v1/live/perform', { body: controls, headers })
    );
  setFootswitchOverrides = (overrides: FootswitchOverride[]) =>
    this.replaceList(
      p => p.footswitchOverrides,
      (c, headers) => c.PUT('/api/v1/live/footswitch-overrides', { body: overrides, headers })
    );

  /** The modifier on a parameter, if any. */
  modifierOf(block: string, param: string): PresetModifier | undefined {
    return this.preset()?.modifiers.find(m => m.block === block && m.param === param);
  }

  /** Adds, replaces (same parameter) or, with null, removes one modifier. */
  putModifier(block: string, param: string, modifier: PresetModifier | null): Promise<boolean> {
    const others = (this.preset()?.modifiers ?? []).filter(m => !(m.block === block && m.param === param));
    return this.setModifiers(modifier ? [...others, modifier] : others);
  }

  /** Taps the tempo; a refusal (an engine that is not connected) is reported. Resolves to whether it worked. */
  async tap(): Promise<boolean> {
    const done = await this.notify.run(async () => {
      await this.connection.client.POST('/api/v1/live/tap');
      return true;
    });
    return done ?? false;
  }

  /** The working copy's name, author, tags and description (one undoable edit); a field left out stays as it is. */
  updateInfo = (info: { name?: string; author?: string; tags?: string[]; description?: string }) =>
    this.mutate(c =>
      c.PATCH('/api/v1/live/info', {
        body: {
          name: info.name ?? null,
          author: info.author ?? null,
          tags: info.tags ?? null,
          description: info.description ?? null
        }
      })
    );
  renameScene = (scene: number, name: string) =>
    this.mutate(c =>
      c.PATCH('/api/v1/live/scenes/{scene}', {
        params: { path: { scene } },
        body: { name, level: null, controllers: null, midi: null }
      })
    );

  /**
   * Replaces a whole list of the working copy (modifiers, perform page, footswitch overrides) that the caller made from
   * the state held, guarded with `If-Match` on that state's revision. When the rig changed meanwhile (412), the state
   * is read again: if the list itself is as it was, the change is sent again on the new revision (a knob or scene
   * change elsewhere loses nothing); if another client or the rig changed the list, the change is not applied and the
   * user is told, rather than their edit silently deleting the other one.
   */
  private async replaceList(
    list: (preset: Preset) => unknown,
    call: (client: ConnectionService['client'], headers: Record<string, string>) => MutationResult
  ): Promise<boolean> {
    const held = this.state();
    if (!held) {
      return this.mutate(c => call(c, {}));
    }

    const base = JSON.stringify(list(held.preset) ?? null);
    for (let attempt = 0; ; attempt++) {
      const revision = this.state()!.revision;
      try {
        this.apply(unwrap(await call(this.connection.client, { 'If-Match': `"${revision}"` })));
        return true;
      } catch (error) {
        if (!(error instanceof ApiError && error.status === 412)) {
          this.notify.error(error);
          return false;
        }
      }

      await this.refresh();
      const fresh = this.state();
      if (!fresh || fresh.preset.id !== held.preset.id || JSON.stringify(list(fresh.preset) ?? null) !== base) {
        this.notify.error(
          new Error(
            'Another editor or the rig changed this meanwhile; the editor now shows that. Make your change again.'
          )
        );
        return false;
      }

      if (attempt + 1 >= ListRetries) {
        this.notify.error(new Error('The rig kept changing; your change was not applied. Try again.'));
        return false;
      }
    }
  }

  /** Runs a mutation, adopts the state it answers with, and reports a failure. Resolves to whether it worked. */
  async mutate(call: (client: ConnectionService['client']) => MutationResult): Promise<boolean> {
    try {
      this.apply(unwrap(await call(this.connection.client)));
      return true;
    } catch (error) {
      this.notify.error(error);
      return false;
    }
  }

  /** Adopts a state unless it is older than the one held; `resync`: the first read after a reconnection. */
  private apply(state: LiveState, resync = false): void {
    const current = this.state();
    if (resync) {
      this.resync = false;
    } else if (current && state.revision < current.revision) {
      return; // an older answer that overtook a newer one
    }

    this.state.set(state);
    if (this.selectedId() && !state.preset.blocks.some(b => b.id === this.selectedId())) {
      this.selectedId.set(null);
    }
  }

  private reconnected(): void {
    this.resync = true;
    this.heard = 0; // revisions announced before the reconnection may belong to a Server that is gone
    void this.refresh();
  }

  private async catchUp(): Promise<void> {
    while (this.again || this.behind()) {
      this.again = false;
      const resync = this.resync;
      try {
        this.apply(unwrap(await this.connection.client.GET('/api/v1/live')), resync);
      } catch (error) {
        this.notify.error(error);
        return;
      }
    }
  }

  /** Whether an event has announced a revision newer than the state held. */
  private behind(): boolean {
    const current = this.state();
    return !current || current.revision < this.heard;
  }

  private onEvent(event: RigEvent): void {
    this.heard = Math.max(this.heard, event.revision);
    // While a read is on the wire, catchUp looks at `heard` when it returns.
    if (!this.refreshing && (this.resync || this.behind())) {
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
    if (batch.presetId !== this.preset()?.id) {
      // Another preset was loaded (a program change, another editor) before these were sent: they belong to the old
      // one, and its block ids may name different blocks in the new one.
      this.overrides.update(map => new Map([...map].filter(([name]) => !name.startsWith(`${key}/`))));
      return;
    }

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
