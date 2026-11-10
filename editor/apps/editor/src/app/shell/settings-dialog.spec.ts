import { Component, DestroyRef, inject, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SetupPage } from '../pages/setup-page';
import { type SettingsTab, UiStore } from '../state/ui.store';
import { PreferencesPanel } from './preferences-panel';
import { SettingsDialog } from './settings-dialog';
import { ShortcutList } from './shortcut-list';

// The Audio setup page polls the Server while it lives, so it must live only while its tab shows (#184).
describe('SettingsDialog', () => {
  const alive = { preferences: 0, setup: 0, shortcuts: 0 };

  /** Counts itself in while it lives. */
  function count(tab: SettingsTab): true {
    alive[tab]++;
    inject(DestroyRef).onDestroy(() => alive[tab]--);
    return true;
  }

  @Component({ selector: 'cth-preferences-panel', template: '' })
  class PreferencesStub {
    readonly counted = count('preferences');
  }

  @Component({ selector: 'cth-setup-page', template: '' })
  class SetupStub {
    readonly counted = count('setup');
  }

  @Component({ selector: 'cth-shortcut-list', template: '' })
  class ShortcutsStub {
    readonly counted = count('shortcuts');
  }

  function render() {
    alive.preferences = alive.setup = alive.shortcuts = 0;
    const settings = signal<SettingsTab | null>(null);
    TestBed.configureTestingModule({ providers: [{ provide: UiStore, useValue: { settings } }] });
    TestBed.overrideComponent(SettingsDialog, {
      remove: { imports: [PreferencesPanel, SetupPage, ShortcutList] },
      add: { imports: [PreferencesStub, SetupStub, ShortcutsStub] }
    });
    const fixture = TestBed.createComponent(SettingsDialog);
    const show = (tab: SettingsTab | null) => {
      settings.set(tab);
      fixture.detectChanges();
    };
    fixture.detectChanges();
    return { show, fixture };
  }

  it('creates nothing while closed', () => {
    const { fixture } = render();
    expect(alive).toEqual({ preferences: 0, setup: 0, shortcuts: 0 });
    fixture.destroy();
  });

  it('creates only the tab that shows, and destroys it when another shows or the dialog closes', () => {
    const { show, fixture } = render();

    show('preferences');
    expect(alive).toEqual({ preferences: 1, setup: 0, shortcuts: 0 });

    show('setup');
    expect(alive).toEqual({ preferences: 0, setup: 1, shortcuts: 0 });

    show('shortcuts');
    expect(alive).toEqual({ preferences: 0, setup: 0, shortcuts: 1 });

    show(null);
    expect(alive).toEqual({ preferences: 0, setup: 0, shortcuts: 0 });
    fixture.destroy();
  });
});
