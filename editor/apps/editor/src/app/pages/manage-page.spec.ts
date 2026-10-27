import { computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import type { Bank, PresetSummary } from '@cathode/api-client';
import { ConnectionService } from '../core/connection';
import { Notify } from '../core/notify';
import { LibraryStore } from '../state/library.store';
import { LiveStore } from '../state/live.store';
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
