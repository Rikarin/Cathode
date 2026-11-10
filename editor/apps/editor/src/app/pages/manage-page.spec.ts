import { computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ApiError, type Bank, type PresetSummary, type Setlist } from '@cathode/api-client';
import { ConnectionService } from '../core/connection';
import { type AsksBeforeLeaving, askBeforeLeaving } from '../core/leave-guard';
import { Notify } from '../core/notify';
import { LibraryStore } from '../state/library.store';
import { LiveStore } from '../state/live.store';
import type { ManageSession } from './manage';
import { ManagePage } from './manage-page';

/** Lets the virtual scroll viewport measure itself and render its range, which takes a few turns. */
async function settle(fixture: { detectChanges(): void; whenStable(): Promise<unknown> }): Promise<void> {
  for (let i = 0; i < 3; i++) {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise(resolve => setTimeout(resolve));
  }

  fixture.detectChanges();
}

describe('ManagePage Library list', () => {
  async function render(count: number) {
    const presets = signal(
      Array.from(
        { length: count },
        (_, i) => ({ id: `p${i}`, name: `Rig ${String(i).padStart(3, '0')}`, tags: [] }) as unknown as PresetSummary
      )
    );
    const library = {
      presets,
      ordered: computed(() => [...presets()].sort((a, b) => a.name.localeCompare(b.name))),
      banks: signal<Bank[]>([]),
      refreshPresets: () => Promise.resolve(),
      refreshBanks: () => Promise.resolve()
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: LibraryStore, useValue: library },
        { provide: LiveStore, useValue: {} },
        { provide: ConnectionService, useValue: {} },
        { provide: Notify, useValue: { error: () => undefined, success: () => undefined } }
      ]
    });
    const fixture = TestBed.createComponent(ManagePage);
    await settle(fixture);
    const element = fixture.nativeElement as HTMLElement;
    const list = element.querySelector<HTMLElement>('[data-testid="library-presets"]');
    const entries = () => [...(list?.querySelectorAll<HTMLElement>('[role="listitem"]') ?? [])];
    return { element, list, entries };
  }

  it('renders the presets to place through the virtual scroll viewport, only a window of a long list', async () => {
    const { list, entries } = await render(500);

    expect(list?.tagName.toLowerCase()).toBe('cdk-virtual-scroll-viewport');
    expect(list?.getAttribute('role')).toBe('list');
    expect(list?.getAttribute('aria-label')).toBe('Presets to place');
    expect(entries().length).toBeGreaterThan(0);
    expect(entries().length).toBeLessThan(500);
  });

  it('keeps each entry draggable, with its place in the whole list', async () => {
    const { entries } = await render(3);

    expect(entries().map(e => e.textContent.trim())).toEqual(['Rig 000', 'Rig 001', 'Rig 002']);
    expect(entries()[1]?.getAttribute('draggable')).toBe('true');
    expect(entries()[1]?.getAttribute('aria-posinset')).toBe('2');
    expect(entries()[1]?.getAttribute('aria-setsize')).toBe('3');
  });
});

describe('ManagePage Save', () => {
  /** The page over a Server that refuses the requests `refuse` picks; `sent` lists every request made. */
  async function render(setlists: Setlist[] = []) {
    const preset = (id: string, name: string) => ({ id, name, tags: [] }) as unknown as PresetSummary;
    const presets = signal([preset('a', 'Alpha'), preset('b', 'Bravo'), preset('c', 'Charlie')]);
    const banks = signal<Bank[]>([{ id: 'gig', name: 'Gig', slots: ['a', 'b', 'c'] }]);
    const library = {
      presets,
      ordered: presets,
      banks,
      setlists: signal(setlists),
      refreshPresets: () => Promise.resolve(),
      refreshBanks: () => Promise.resolve(),
      refreshSetlists: () => Promise.resolve()
    };
    const sent: string[] = [];
    const server = { refuse: (_request: string) => false };
    const request = (method: string) => (path: string, options: { params: { path: { id: string } } }) => {
      const made = `${method} ${path.replace('{id}', options.params.path.id)}`;
      sent.push(made);
      return server.refuse(made)
        ? Promise.reject(new ApiError(409, 'Refused.'))
        : Promise.resolve({ data: {}, response: new Response('{}') });
    };
    const errors: string[] = [];
    const successes: string[] = [];
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: LibraryStore, useValue: library },
        { provide: LiveStore, useValue: {} },
        {
          provide: ConnectionService,
          useValue: {
            client: { POST: request('POST'), PATCH: request('PATCH'), PUT: request('PUT'), DELETE: request('DELETE') }
          }
        },
        {
          provide: Notify,
          useValue: { error: (e: Error) => errors.push(e.message), success: (m: string) => successes.push(m) }
        }
      ]
    });
    const fixture = TestBed.createComponent(ManagePage);
    await settle(fixture);
    const page = fixture.componentInstance as unknown as {
      session(): ManageSession;
      save(): Promise<void>;
      dirty(): boolean;
      changed(): void;
    };
    return { page, fixture, sent, server, errors, successes };
  }

  it('keeps what a failed Save had not stored yet, names the step, and sends only that on the next Save', async () => {
    const { page, sent, server, errors, successes } = await render();
    const session = page.session();
    session.rename('a', 'Able');
    session.rename('b', 'Baker');
    session.swap({ bank: 'gig', slot: 0 }, { bank: 'gig', slot: 1 });
    session.delete(['c']);
    page.changed();

    server.refuse = request => request === 'PATCH /api/v1/presets/b/metadata';
    await page.save();

    expect(sent).toEqual(['PATCH /api/v1/presets/a/metadata', 'PATCH /api/v1/presets/b/metadata']);
    expect(errors).toHaveLength(1);
    expect(errors[0]).toContain('Changing “Bravo” failed: Refused.');
    expect(successes).toEqual([]);
    expect(page.session()).toBe(session); // not replaced by a fresh one
    expect(page.dirty()).toBe(true);
    expect(session.plan().meta).toEqual([['b', { name: 'Baker' }]]);
    expect(session.plan().banks.map(b => b.slots)).toEqual([['b', 'a']]);
    expect(session.plan().deletes).toEqual(['c']);

    sent.length = 0;
    server.refuse = () => false;
    await page.save();

    expect(sent).toEqual(['PATCH /api/v1/presets/b/metadata', 'PUT /api/v1/banks/gig', 'DELETE /api/v1/presets/c']);
    expect(successes).toEqual(['Saved.']);
    expect(page.dirty()).toBe(false);
  });

  it('asks before it is left with unsaved changes, and not otherwise', async () => {
    const { page, fixture } = await render();
    const guarded = page as unknown as {
      canLeave(): boolean | Promise<boolean>;
      confirmLeave(): boolean;
      answerLeave(leave: boolean): void;
      beforeUnload(event: Event): void;
    };
    expect(guarded.canLeave()).toBe(true);
    const clean = new Event('beforeunload', { cancelable: true });
    guarded.beforeUnload(clean);
    expect(clean.defaultPrevented).toBe(false);

    page.session().rename('a', 'Able');
    page.changed();

    const stay = guarded.canLeave();
    expect(guarded.confirmLeave()).toBe(true);
    guarded.answerLeave(false);
    await expect(stay).resolves.toBe(false);
    expect(page.session().plan().meta).toEqual([['a', { name: 'Able' }]]);

    const leave = guarded.canLeave();
    guarded.answerLeave(true);
    await expect(leave).resolves.toBe(true);

    // Reloading or closing the tab: the browser asks.
    const unload = new Event('beforeunload', { cancelable: true });
    guarded.beforeUnload(unload);
    expect(unload.defaultPrevented).toBe(true);
    fixture.destroy();
  });

  it('keeps the router on the page until the Discard question is answered', async () => {
    const { page } = await render();
    const guarded = page as unknown as AsksBeforeLeaving & { answerLeave(leave: boolean): void };
    const guard = (asked: AsksBeforeLeaving) =>
      TestBed.runInInjectionContext(() => askBeforeLeaving(asked, null as never, null as never, null as never));
    expect(guard(guarded)).toBe(true);

    page.session().rename('a', 'Able');
    page.changed();
    const answer = guard(guarded) as Promise<boolean>;
    guarded.answerLeave(false);

    await expect(answer).resolves.toBe(false);
  });

  it('does not ask the Server to delete a preset a setlist holds: it saves the rest and says where it is used', async () => {
    const { page, sent, errors, successes } = await render([
      {
        id: 'friday',
        name: 'Friday',
        entries: [
          { presetId: 'b', scene: 0 },
          { presetId: 'c', scene: 0 }
        ]
      }
    ]);
    page.session().delete(['a', 'c']);
    page.changed();

    await page.save();

    expect(sent).toEqual(['PUT /api/v1/banks/gig', 'DELETE /api/v1/presets/a']);
    expect(errors).toEqual([
      'Not deleted: “Charlie” (setlist “Friday”, entry 2). Take a preset out of its setlists to delete it.'
    ]);
    expect(successes).toEqual([]);
    expect(page.dirty()).toBe(false);
  });
});
