import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { ParameterInfo, PresetBlock } from '@cathode/api-client';
import { ConnectionService } from '../core/connection';
import { Notify } from '../core/notify';
import { LibraryStore } from '../state/library.store';
import { LiveStore } from '../state/live.store';
import { type ParamTarget, UiStore } from '../state/ui.store';
import { BatchSetDialog } from './batch-set-dialog';

describe('BatchSetDialog', () => {
  const target: ParamTarget = {
    block: { id: 'delay1', type: 'delay' } as PresetBlock,
    param: { id: 'mix', name: 'Mix', unit: '%' } as ParameterInfo,
    channel: 'B',
    value: 80
  };

  function render(result: { changed: string[]; skipped: string[] } | 'refused' = { changed: ['p2'], skipped: [] }) {
    const batchSet = signal<ParamTarget | null>(null);
    const refreshPresets = vi.fn(() => Promise.resolve());
    const post = vi.fn((_path: string, _init: { body: object }) =>
      Promise.resolve(
        result === 'refused'
          ? { error: { title: 'No such block' }, response: new Response(null, { status: 400 }) }
          : { data: result, response: new Response(null, { status: 200 }) }
      )
    );
    const success = vi.fn();
    const error = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        { provide: UiStore, useValue: { batchSet } },
        {
          provide: LibraryStore,
          useValue: {
            presets: signal([
              { id: 'p1', name: 'Loaded' },
              { id: 'p2', name: 'Clean', tags: [] },
              { id: 'p3', name: 'Lead', favorite: true }
            ]),
            refreshPresets
          }
        },
        { provide: LiveStore, useValue: { preset: signal({ id: 'p1' }) } },
        { provide: ConnectionService, useValue: { client: { POST: post } } },
        {
          provide: Notify,
          useValue: {
            success,
            error,
            run: async (action: () => Promise<unknown>) => {
              try {
                return await action();
              } catch (failure) {
                error(failure);
                return undefined;
              }
            }
          }
        }
      ]
    });
    TestBed.overrideComponent(BatchSetDialog, { set: { template: '', imports: [] } });
    const fixture = TestBed.createComponent(BatchSetDialog);
    fixture.detectChanges();
    const dialog = fixture.componentInstance as unknown as {
      query: { set(value: string): void };
      scope: { set(value: 'channel' | 'all'): void };
      scopes(): { label: string }[];
      chosen(): ReadonlySet<string>;
      shown(): { id: string }[];
      toggle(id: string): void;
      apply(): Promise<void>;
    };
    const open = () => {
      batchSet.set({ ...target });
      fixture.detectChanges();
    };
    return { dialog, open, batchSet, refreshPresets, post, success, error };
  }

  it('lists every preset but the loaded one, favourites first, and filters by the search', () => {
    const { dialog, open } = render();
    open();
    expect(dialog.shown().map(p => p.id)).toEqual(['p3', 'p2']);

    dialog.query.set('cle');
    expect(dialog.shown().map(p => p.id)).toEqual(['p2']);
  });

  it('opens with nothing chosen and a fresh list, each time', () => {
    const { dialog, open, batchSet, refreshPresets } = render();
    open();
    dialog.toggle('p2');
    dialog.query.set('x');
    expect(dialog.chosen().has('p2')).toBe(true);

    batchSet.set(null);
    open();

    expect(dialog.chosen().size).toBe(0);
    expect(dialog.shown()).toHaveLength(2);
    expect(refreshPresets).toHaveBeenCalledTimes(2);
  });

  it('ticks and unticks a preset', () => {
    const { dialog, open } = render();
    open();

    dialog.toggle('p2');
    dialog.toggle('p3');
    dialog.toggle('p2');

    expect([...dialog.chosen()]).toEqual(['p3']);
  });

  it('names the channel the value comes from', () => {
    const { dialog, open } = render();
    open();
    expect(dialog.scopes().map(s => s.label)).toEqual(['Channel B', 'All channels']);
  });

  it('sends the value for the block’s channel, says what changed and what was skipped, and closes', async () => {
    const { dialog, open, batchSet, post, success } = render({ changed: ['p2'], skipped: ['p3'] });
    open();
    dialog.toggle('p2');
    dialog.toggle('p3');

    await dialog.apply();

    expect(post).toHaveBeenCalledWith('/api/v1/presets/batch-set', {
      body: { presets: ['p2', 'p3'], block: 'delay1', type: 'delay', param: 'mix', value: 80, channel: 'B' }
    });
    expect(success).toHaveBeenCalledWith('Set in 1 preset(s); 1 without delay1 skipped.');
    expect(batchSet()).toBeNull();
  });

  it('sends no channel for all channels', async () => {
    const { dialog, open, post } = render();
    open();
    dialog.toggle('p2');
    dialog.scope.set('all');

    await dialog.apply();

    expect(post.mock.calls[0]?.[1]).toMatchObject({ body: { channel: null } });
  });

  it('stays open with the choice kept when the Server refuses', async () => {
    const { dialog, open, batchSet, success, error } = render('refused');
    open();
    dialog.toggle('p2');

    await dialog.apply();

    expect(error).toHaveBeenCalledTimes(1);
    expect(success).not.toHaveBeenCalled();
    expect(batchSet()).not.toBeNull();
    expect(dialog.chosen().has('p2')).toBe(true);
  });
});
