import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { LiveState, Preset, RigEvent } from '@cathode/api-client';
import { Subject } from 'rxjs';
import { EventStream } from '../core/events';
import { CompareStore } from './compare.store';
import { LiveStore } from './live.store';

describe('CompareStore', () => {
  /** A rig whose working copy is preset `id` named `name`, on its first load. */
  function setup() {
    const preset = (id: string, name: string) => ({ id, name }) as unknown as Preset;
    const state = signal<LiveState | null>({ preset: preset('x', 'X'), load: 1 } as unknown as LiveState);
    const edit = (name: string) => state.update(s => s && { ...s, preset: { ...s.preset, name } });
    /** Another preset is loaded, by anyone: a footswitch, a program change, another editor. */
    const load = (id: string, name: string) =>
      state.update(s => s && { ...s, preset: preset(id, name), load: s.load + 1 });
    const live = {
      state,
      preset: () => state()?.preset ?? null,
      replace: vi.fn((next: Preset) => {
        state.update(s => s && { ...s, preset: next });
        return Promise.resolve(true);
      })
    };
    const events$ = new Subject<RigEvent>();
    TestBed.configureTestingModule({
      providers: [
        { provide: LiveStore, useValue: live },
        { provide: EventStream, useValue: { events$ } }
      ]
    });
    const compare = TestBed.inject(CompareStore);
    const working = () => ({ id: state()?.preset.id, name: state()?.preset.name });
    return { compare, live, events$, edit, load, working };
  }

  it('keeps two versions of the working copy, each with its edits', async () => {
    const { compare, edit, working } = setup();
    expect(compare.active()).toBe('A');
    expect(compare.hasB()).toBe(false);

    await compare.switchTo('B'); // B starts as a copy of A
    expect(compare.active()).toBe('B');
    edit('X bright');
    await compare.switchTo('A');
    expect(working()).toEqual({ id: 'x', name: 'X' });
    expect(compare.hasB()).toBe(true);

    await compare.switchTo('B');
    expect(working()).toEqual({ id: 'x', name: 'X bright' });
  });

  it('forgets both versions when another preset is loaded from anywhere', async () => {
    const { compare, live, edit, load, working } = setup();
    await compare.switchTo('B');
    edit('X bright');
    live.replace.mockClear();

    load('y', 'Y'); // not through this editor: nothing called reset()

    expect(compare.active()).toBe('A');
    expect(compare.hasB()).toBe(false);

    // B now starts as a copy of Y; the previous preset's content is not put into it.
    await compare.switchTo('B');
    expect(working()).toEqual({ id: 'y', name: 'Y' });
    await compare.switchTo('A');
    expect(working()).toEqual({ id: 'y', name: 'Y' });
    expect(live.replace.mock.calls.every(([sent]) => sent.name === 'Y')).toBe(true);
  });

  it.each(['presetReverted', 'presetCleared', 'snapshotLoaded'])('forgets both versions on %s', async type => {
    const { compare, events$ } = setup();
    await compare.switchTo('B');

    events$.next({ type, revision: 2 });

    expect(compare.active()).toBe('A');
    expect(compare.hasB()).toBe(false);
  });

  it('keeps them through its own replace and through ordinary edits', async () => {
    const { compare, events$ } = setup();
    await compare.switchTo('B');

    events$.next({ type: 'presetReplaced', revision: 2 });
    events$.next({ type: 'paramChanged', revision: 3 });

    expect(compare.active()).toBe('B');
  });

  it('stays on the slot it was on when the Server refuses the replace', async () => {
    const { compare, live } = setup();
    live.replace.mockImplementationOnce(() => Promise.resolve(false));

    await compare.switchTo('B');

    expect(compare.active()).toBe('A');
  });
});
