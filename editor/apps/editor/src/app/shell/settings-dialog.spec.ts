import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { type SettingsTab, UiStore } from '../state/ui.store';
import { SettingsDialog } from './settings-dialog';

// The dialog's own state only: which tab shows, and that it is open exactly while one does. Its tabs' content
// (Preferences, Audio setup, the shortcut list) is left out here.
describe('SettingsDialog', () => {
  function render() {
    const ui = { settings: signal<SettingsTab | null>(null) };
    TestBed.configureTestingModule({ providers: [{ provide: UiStore, useValue: ui }] });
    TestBed.overrideComponent(SettingsDialog, { set: { template: '', imports: [] } });
    const fixture = TestBed.createComponent(SettingsDialog);
    fixture.detectChanges();
    const dialog = fixture.componentInstance as unknown as {
      open(): boolean;
      select(tab: string | number | null): void;
    };
    return { ui, dialog };
  }

  it('is open while a tab is chosen, and closed with none', () => {
    const { ui, dialog } = render();
    expect(dialog.open()).toBe(false);

    ui.settings.set('shortcuts');
    expect(dialog.open()).toBe(true);

    ui.settings.set(null);
    expect(dialog.open()).toBe(false);
  });

  it('shows the tab that is picked', () => {
    const { ui, dialog } = render();
    ui.settings.set('preferences');

    dialog.select('setup');
    expect(ui.settings()).toBe('setup');

    dialog.select('shortcuts');
    expect(ui.settings()).toBe('shortcuts');
  });

  it('keeps its tab when the tab strip reports something that is not one of its tabs', () => {
    const { ui, dialog } = render();
    ui.settings.set('setup');

    dialog.select(null);
    dialog.select(2);
    dialog.select('network');

    expect(ui.settings()).toBe('setup');
  });
});
