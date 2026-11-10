import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { Preset } from '@cathode/api-client';
import { CompareStore } from './compare.store';
import { LiveStore } from './live.store';

describe('CompareStore', () => {
  function preset(name: string, id = 'p1'): Preset {
    return { id, name, blocks: [] } as unknown as Preset;
  }

  function setup(initial: Preset | null = preset('Clean')) {
    const working = signal<Preset | null>(initial);
    const replaced: Preset[] = [];
    const live = {
      preset: working,
      /** Whether the Server takes the next replace. */
      accepts: true,
      replace: (next: Preset) => {
        if (!live.accepts) {
          return Promise.resolve(false);
        }

        replaced.push(next);
        working.set(next);
        return Promise.resolve(true);
      }
    };
    TestBed.configureTestingModule({ providers: [{ provide: LiveStore, useValue: live }] });
    const compare = TestBed.inject(CompareStore);
    /** An edit of the working copy, as the Server would send it back. */
    const edit = (name: string) => working.update(p => (p ? { ...p, name } : p));
    return { compare, live, working, replaced, edit };
  }

  it('starts on A, with B not captured', () => {
    const { compare } = setup();
    expect(compare.active()).toBe('A');
    expect(compare.hasB()).toBe(false);
  });

  it('starts B as a copy of A the first time it is switched to', async () => {
    const { compare, working, replaced } = setup();
    const before = working();

    await compare.switchTo('B');

    expect(compare.active()).toBe('B');
    expect(replaced).toHaveLength(1);
    expect(replaced[0]).toEqual(before);
    expect(replaced[0]).not.toBe(before);
  });

  it('keeps each slot’s edits across switches', async () => {
    const { compare, working, edit } = setup();

    await compare.switchTo('B');
    edit('Crunch');
    await compare.switchTo('A');
    expect(compare.active()).toBe('A');
    expect(compare.hasB()).toBe(true);
    expect(working()?.name).toBe('Clean');

    edit('Cleaner');
    await compare.switchTo('B');
    expect(working()?.name).toBe('Crunch');

    await compare.switchTo('A');
    expect(working()?.name).toBe('Cleaner');
  });

  it('does nothing when asked for the slot it is on, or with no working copy', async () => {
    const { compare, replaced } = setup();
    await compare.switchTo('A');
    expect(replaced).toEqual([]);

    TestBed.resetTestingModule();
    const empty = setup(null);
    await empty.compare.switchTo('B');
    expect(empty.replaced).toEqual([]);
    expect(empty.compare.active()).toBe('A');
  });

  it('stays on its slot when the Server refuses the other one', async () => {
    const { compare, live, working } = setup();
    live.accepts = false;

    await compare.switchTo('B');

    expect(compare.active()).toBe('A');
    expect(working()?.name).toBe('Clean');
  });

  it('forgets both snapshots on reset: B is a copy of the new working copy again', async () => {
    const { compare, working, edit } = setup();
    await compare.switchTo('B');
    edit('Crunch');
    await compare.switchTo('A');

    compare.reset();
    working.set(preset('Lead', 'p9'));

    expect(compare.active()).toBe('A');
    expect(compare.hasB()).toBe(false);
    await compare.switchTo('B');
    expect(working()).toMatchObject({ id: 'p9', name: 'Lead' });
  });
});
