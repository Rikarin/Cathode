import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { provideIcons } from '@ng-icons/core';
import { matSettingsRound } from '@ng-icons/material-icons/round';
import { XuiDialogImports } from '@xui/dialog';
import { XuiTabsImports } from '@xui/tabs';
import { SetupPage } from '../pages/setup-page';
import { type SettingsTab, UiStore } from '../state/ui.store';
import { PreferencesPanel } from './preferences-panel';
import { ShortcutList } from './shortcut-list';

/**
 * Settings (⌘,), as an app's preferences window: the user's Preferences, the Audio setup (interface, rate, buffer,
 * latency, Server) and the Keyboard shortcuts, one tab each. `?` opens it on the shortcuts.
 */
@Component({
  selector: 'cth-settings-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PreferencesPanel, SetupPage, ShortcutList, XuiDialogImports, XuiTabsImports],
  providers: [provideIcons({ matSettingsRound })],
  template: `
    <xui-dialog
      title="Settings"
      icon="matSettingsRound"
      class="w-[min(64rem,calc(100vw-2rem))] max-w-none"
      [open]="open()"
      (openChange)="$event || ui.settings.set(null)"
    >
      <xui-dialog-body class="overflow-hidden">
        <xui-tabs
          renderActiveTabPanelOnly
          [animate]="false"
          [selectedTabId]="ui.settings() ?? 'preferences'"
          (selectedTabIdChange)="select($event)"
        >
          <xui-tab id="preferences" title="Preferences">
            <cth-preferences-panel class="h-[min(32rem,60vh)] [scrollbar-gutter:stable] overflow-y-auto pt-4 pr-6" />
          </xui-tab>
          <xui-tab id="setup" title="Audio setup">
            <cth-setup-page class="!h-[min(32rem,60vh)] !px-0 !pt-4 !pb-0" />
          </xui-tab>
          <xui-tab id="shortcuts" title="Keyboard shortcuts">
            <cth-shortcut-list class="h-[min(32rem,60vh)] [scrollbar-gutter:stable] overflow-y-auto pt-4 pr-6" />
          </xui-tab>
        </xui-tabs>
      </xui-dialog-body>
    </xui-dialog>
  `
})
export class SettingsDialog {
  protected readonly ui = inject(UiStore);
  protected readonly open = computed(() => this.ui.settings() !== null);

  protected select(tab: string | number | null): void {
    if (tab === 'preferences' || tab === 'setup' || tab === 'shortcuts') {
      this.ui.settings.set(tab satisfies SettingsTab);
    }
  }
}
