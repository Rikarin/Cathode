import { TestBed } from '@angular/core/testing';
import type { AssetInfo } from '@cathode/api-client';
import { LibraryStore } from '../../state/library.store';
import { AssetPicker } from './asset-picker';

describe('AssetPicker with an empty library', () => {
  const capture = { ref: 'sha256:a', kind: 'nam', name: 'Plexi.nam', favorite: false } as AssetInfo;

  /** The picker once the library has answered (`listing` resolves), or while it has not. */
  async function render(assets: AssetInfo[], hint: string | undefined, listing = Promise.resolve()) {
    TestBed.configureTestingModule({
      providers: [{ provide: LibraryStore, useValue: { refreshAssets: () => listing } }]
    });
    const fixture = TestBed.createComponent(AssetPicker);
    fixture.componentRef.setInput('label', 'Capture');
    fixture.componentRef.setInput('assets', assets);
    fixture.componentRef.setInput('hint', hint);
    fixture.detectChanges();
    await Promise.resolve();
    await fixture.whenStable();
    fixture.detectChanges();
    return (fixture.nativeElement as HTMLElement).querySelector('[data-testid="asset-hint"]');
  }

  it('says that Cathode ships no files and what to do, instead of an unexplained empty list', async () => {
    const hint = await render([], 'Cathode comes with no captures. Upload a .nam file.');

    expect(hint?.textContent).toContain('Cathode comes with no captures');
  });

  it('says nothing once the library has a file of the family', async () => {
    expect(await render([capture], 'Cathode comes with no captures.')).toBeNull();
  });

  it('says nothing before the library has been read: the list is not yet known to be empty', async () => {
    expect(await render([], 'Cathode comes with no captures.', new Promise<void>(() => undefined))).toBeNull();
  });

  it('says nothing where no hint is given (a reference recording is not factory content)', async () => {
    expect(await render([], undefined)).toBeNull();
  });
});
