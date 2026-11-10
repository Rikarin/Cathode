import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { ConnectionService } from '../core/connection';
import { Notify } from '../core/notify';
import { CompareStore } from '../state/compare.store';
import { LibraryStore } from '../state/library.store';
import { LiveStore } from '../state/live.store';
import { PresetSwitcher } from '../state/preset-switcher';
import { PresetMenu } from './preset-menu';

function render(preset: { id?: string; name: string } | undefined = { id: 'p1', name: 'Lead' }) {
  const download = vi.fn(() => Promise.resolve());
  TestBed.configureTestingModule({
    providers: [
      { provide: LiveStore, useValue: { preset: signal(preset), state: signal(undefined) } },
      { provide: Router, useValue: {} },
      { provide: CompareStore, useValue: {} },
      { provide: PresetSwitcher, useValue: {} },
      { provide: ConnectionService, useValue: { client: {} } },
      { provide: Notify, useValue: { run: (action: () => Promise<unknown>) => action() } },
      { provide: LibraryStore, useValue: { download } }
    ]
  });
  TestBed.overrideComponent(PresetMenu, { set: { template: '', imports: [] } });
  const fixture = TestBed.createComponent(PresetMenu);
  fixture.detectChanges();
  const menu = fixture.componentInstance as unknown as { export: () => void };
  return { menu, download };
}

describe('PresetMenu export', () => {
  it('downloads the working copy from GET /live/export, named after the preset', () => {
    const { menu, download } = render();
    menu.export();
    expect(download).toHaveBeenCalledWith('/api/v1/live/export', 'Lead.cthpreset');
  });

  it('falls back to a plain file name for a preset without a name', () => {
    const { menu, download } = render({ name: '  ' });
    menu.export();
    expect(download).toHaveBeenCalledWith('/api/v1/live/export', 'preset.cthpreset');
  });
});
