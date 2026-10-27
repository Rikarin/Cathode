import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { AssetInfo } from '@cathode/api-client';
import { Notify } from '../core/notify';
import { LibraryStore } from '../state/library.store';
import { AssetLibrary, AssetList } from './asset-library';

function asset(index: number): AssetInfo {
  const name = `IR ${String(index).padStart(3, '0')}.wav`;
  return {
    ref: `ir${index}`,
    kind: 'ir',
    name,
    size: 4096,
    added: '2026-01-01T00:00:00Z',
    favorite: false
  };
}

function setup(count: number, usage: ReadonlyMap<string, readonly string[]> = new Map()) {
  const library = {
    models: signal<AssetInfo[]>([]),
    cabGrids: signal<AssetInfo[]>(Array.from({ length: count }, (_, i) => asset(i))),
    usage: signal(usage),
    setAssetMetadata: vi.fn(() => Promise.resolve(true))
  };
  TestBed.configureTestingModule({
    providers: [
      AssetLibrary,
      { provide: LibraryStore, useValue: library },
      { provide: Notify, useValue: { error: () => undefined, success: () => undefined } }
    ]
  });
  const assets = TestBed.inject(AssetLibrary);
  assets.show('cabs');
  return { library, assets };
}

describe('AssetLibrary usage', () => {
  it('maps each used asset to its presets, with the names joined once', () => {
    const { assets } = setup(
      3,
      new Map([
        ['ir1', ['Clean', 'Lead']],
        ['ir2', []]
      ])
    );

    expect(assets.uses().get('ir1')).toEqual({ presets: ['Clean', 'Lead'], names: 'Clean, Lead' });
    expect(assets.uses().has('ir2')).toBe(false); // used by nothing: no entry
    expect(assets.usedBy(asset(1))).toEqual(['Clean', 'Lead']);
    expect(assets.usedBy(asset(0))).toEqual([]);
  });

  it('is computed once per usage change, not again when the search or the selection changes', () => {
    const { library, assets } = setup(3, new Map([['ir1', ['Clean']]]));
    const first = assets.uses();

    assets.query.set('IR 001');
    assets.selected.set(new Set(['ir1']));
    expect(assets.uses()).toBe(first);

    library.usage.set(new Map([['ir1', ['Clean', 'Crunch']]]));
    expect(assets.uses()).not.toBe(first);
    expect(assets.uses().get('ir1')?.names).toBe('Clean, Crunch');
  });
});

/** Lets the virtual scroll viewport measure itself and render its range, which takes a few turns. */
async function settle(fixture: { detectChanges(): void; whenStable(): Promise<unknown> }): Promise<void> {
  for (let i = 0; i < 3; i++) {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise(resolve => setTimeout(resolve));
  }

  fixture.detectChanges();
}

describe('AssetList', () => {
  async function render(count: number, usage?: ReadonlyMap<string, readonly string[]>) {
    const context = setup(count, usage);
    const fixture = TestBed.createComponent(AssetList);
    await settle(fixture);
    const element = fixture.nativeElement as HTMLElement;
    const rows = () => [...element.querySelectorAll<HTMLElement>('[data-testid^="asset-row-"]')];
    return { ...context, fixture, element, rows };
  }

  it('renders its rows through the virtual scroll viewport, only a window of a long list', async () => {
    const { element, rows } = await render(500);

    const viewport = element.querySelector('cdk-virtual-scroll-viewport[data-testid="asset-viewport"]');
    expect(viewport).not.toBeNull();
    expect(rows().length).toBeGreaterThan(0);
    expect(rows().length).toBeLessThan(500);
    expect(rows().every(row => viewport?.contains(row))).toBe(true);
    expect(rows()[0]?.dataset['testid']).toBe('asset-row-ir0');
  });

  it('keeps the table semantics: a row count, and each row its position', async () => {
    const { element, rows } = await render(500);

    const table = element.querySelector('[data-testid="asset-list"]');
    expect(table?.getAttribute('role')).toBe('table');
    expect(table?.getAttribute('aria-rowcount')).toBe('501');
    expect(rows()[0]?.getAttribute('role')).toBe('row');
    expect(rows()[0]?.getAttribute('aria-rowindex')).toBe('2'); // the header is row 1
  });

  it('selects the row clicked, by its index in the whole list', async () => {
    const { assets, fixture, rows } = await render(3);

    rows()[1]?.click();
    fixture.detectChanges();

    expect([...assets.selected()]).toEqual(['ir1']);
    expect(rows()[1]?.getAttribute('aria-selected')).toBe('true');
  });

  it('shows how many presets use an asset', async () => {
    const { rows } = await render(3, new Map([['ir2', ['Clean', 'Lead']]]));

    expect(rows()[2]?.textContent).toContain('2 presets');
    expect(rows()[0]?.textContent).toContain('—');
  });

  it('says when the search matches nothing', async () => {
    const { assets, fixture, element, rows } = await render(3);

    assets.query.set('nothing like it');
    await settle(fixture);

    expect(rows()).toHaveLength(0);
    expect(element.textContent).toContain('Nothing matches');
  });
});
