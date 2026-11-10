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
  const remove = vi.fn(() => Promise.resolve({ response: new Response(null, { status: 204 }) }));
  const list = vi.fn(() => Promise.resolve({ data: [], response: new Response(null, { status: 200 }) }));
  TestBed.configureTestingModule({
    providers: [
      { provide: LiveStore, useValue: { preset: signal(preset), state: signal(undefined) } },
      { provide: Router, useValue: {} },
      { provide: CompareStore, useValue: {} },
      { provide: PresetSwitcher, useValue: {} },
      { provide: ConnectionService, useValue: { client: { DELETE: remove, GET: list } } },
      { provide: Notify, useValue: { run: (action: () => Promise<unknown>) => action() } },
      { provide: LibraryStore, useValue: { download } }
    ]
  });
  TestBed.overrideComponent(PresetMenu, { set: { template: '', imports: [] } });
  const fixture = TestBed.createComponent(PresetMenu);
  fixture.detectChanges();
  const menu = fixture.componentInstance as unknown as {
    export: () => void;
    askDeleteSnapshot: (snapshot: { id: string; name: string }) => void;
    deleteSnapshot: () => Promise<void>;
    confirmDeleteSnapshot: () => boolean;
    askDeleteTemplate: (template: { id: string; name: string }) => void;
    deleteTemplate: () => Promise<void>;
    confirmDeleteTemplate: () => boolean;
  };
  return { menu, download, remove, list };
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

describe('PresetMenu deletes', () => {
  it('asks before deleting a snapshot, and deletes the one asked about once confirmed', async () => {
    const { menu, remove, list } = render();
    menu.askDeleteSnapshot({ id: 's1', name: 'Before the fuzz' });
    expect(menu.confirmDeleteSnapshot()).toBe(true);
    expect(remove).not.toHaveBeenCalled();

    await menu.deleteSnapshot();
    expect(remove).toHaveBeenCalledTimes(1);
    expect(remove).toHaveBeenCalledWith('/api/v1/snapshots/{id}', { params: { path: { id: 's1' } } });
    expect(list).toHaveBeenCalledWith('/api/v1/snapshots', { params: { query: { preset: 'p1' } } });
  });

  it('asks before deleting a template, and deletes the one asked about once confirmed', async () => {
    const { menu, remove, list } = render();
    menu.askDeleteTemplate({ id: 't1', name: 'Clean start' });
    expect(menu.confirmDeleteTemplate()).toBe(true);
    expect(remove).not.toHaveBeenCalled();

    await menu.deleteTemplate();
    expect(remove).toHaveBeenCalledTimes(1);
    expect(remove).toHaveBeenCalledWith('/api/v1/templates/{id}', { params: { path: { id: 't1' } } });
    expect(list).toHaveBeenCalledWith('/api/v1/templates');
  });

  it('deletes nothing without a snapshot or template asked about', async () => {
    const { menu, remove } = render();
    await menu.deleteSnapshot();
    await menu.deleteTemplate();
    expect(remove).not.toHaveBeenCalled();
  });
});
