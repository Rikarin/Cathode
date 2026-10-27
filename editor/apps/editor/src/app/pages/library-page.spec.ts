import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import type { AssetInfo, PresetSummary } from '@cathode/api-client';
import { Notify } from '../core/notify';
import { BlockPresetsStore } from '../state/block-presets.store';
import { CatalogStore } from '../state/catalog.store';
import { LibraryStore } from '../state/library.store';
import { LiveStore } from '../state/live.store';
import { PresetSwitcher } from '../state/preset-switcher';
import { LibraryPage } from './library-page';

function preset(index: number, factory = false): PresetSummary {
  return {
    id: `${factory ? 'f' : 'p'}${index}`,
    name: `${factory ? 'Factory' : 'Rig'} ${String(index).padStart(3, '0')}`,
    author: 'Me',
    tags: index === 0 ? ['clean', 'live'] : [],
    favorite: false,
    factory,
    modified: '2026-01-01T00:00:00Z'
  };
}

/** Lets the virtual scroll viewport measure itself and render its range, which takes a few turns. */
async function settle(fixture: { detectChanges(): void; whenStable(): Promise<unknown> }): Promise<void> {
  for (let i = 0; i < 3; i++) {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise(resolve => setTimeout(resolve));
  }

  fixture.detectChanges();
}

describe('LibraryPage preset list', () => {
  async function render(presets: PresetSummary[]) {
    const library = {
      presets: signal(presets),
      models: signal<AssetInfo[]>([]),
      cabGrids: signal<AssetInfo[]>([]),
      usage: signal(new Map<string, readonly string[]>()),
      refreshPresets: () => Promise.resolve(),
      refreshAssets: () => Promise.resolve(),
      refreshUsage: () => Promise.resolve(),
      setPresetMetadata: vi.fn(() => Promise.resolve(true))
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: LibraryStore, useValue: library },
        {
          provide: BlockPresetsStore,
          useValue: { presets: signal([]), user: signal([]), folders: signal([]), refresh: () => Promise.resolve() }
        },
        { provide: CatalogStore, useValue: { info: () => undefined } },
        { provide: LiveStore, useValue: { preset: () => null } },
        { provide: PresetSwitcher, useValue: { mayReplace: () => false } },
        { provide: Notify, useValue: { error: () => undefined, success: () => undefined } }
      ]
    });
    const fixture = TestBed.createComponent(LibraryPage);
    await settle(fixture);
    const element = fixture.nativeElement as HTMLElement;
    const rows = () => [...element.querySelectorAll<HTMLElement>('[data-testid^="preset-row-"]')];
    return { library, fixture, element, rows };
  }

  it('renders its rows through the virtual scroll viewport, only a window of a long list', async () => {
    const { element, rows } = await render([
      ...Array.from({ length: 50 }, (_, i) => preset(i)),
      ...Array.from({ length: 400 }, (_, i) => preset(i, true))
    ]);

    const viewport = element.querySelector('cdk-virtual-scroll-viewport[data-testid="preset-viewport"]');
    expect(viewport).not.toBeNull();
    expect(rows().length).toBeGreaterThan(0);
    expect(rows().length).toBeLessThan(450);
    expect(rows().every(row => viewport?.contains(row))).toBe(true);
    // The user's presets by name first, then the factory bank.
    expect(rows()[0]?.dataset['testid']).toBe('preset-row-p0');
    expect(element.querySelector('[data-testid="preset-list"]')?.getAttribute('aria-rowcount')).toBe('451');
  });

  it('keeps a row on one line: its tags beside the name', async () => {
    const { rows } = await render([preset(0)]);

    expect(rows()[0]?.classList).toContain('h-12');
    expect(rows()[0]?.textContent).toContain('clean');
    expect(rows()[0]?.getAttribute('aria-rowindex')).toBe('2');
  });

  it('selects the row clicked and names it in the footer', async () => {
    const { fixture, element, rows } = await render([preset(0), preset(1)]);

    rows()[1]?.click();
    fixture.detectChanges();

    expect(rows()[1]?.getAttribute('aria-selected')).toBe('true');
    expect(rows()[0]?.getAttribute('aria-selected')).toBe('false');
    expect(element.querySelector('footer')?.textContent).toContain('Rig 001');
  });

  it('filters by the search, and says when nothing matches', async () => {
    const { fixture, element, rows } = await render([preset(0), preset(1), preset(2)]);
    const search = element.querySelector<HTMLInputElement>('input[aria-label="Search presets"]');
    if (!search) {
      throw new Error('No search field');
    }

    search.value = 'rig 002';
    search.dispatchEvent(new Event('input'));
    await settle(fixture);
    expect(rows().map(row => row.dataset['testid'])).toEqual(['preset-row-p2']);

    search.value = 'nothing like it';
    search.dispatchEvent(new Event('input'));
    await settle(fixture);
    expect(rows()).toHaveLength(0);
    expect(element.textContent).toContain('No preset matches');
  });
});
