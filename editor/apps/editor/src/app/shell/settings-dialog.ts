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
        <!--
          Each tab scrolls on its own; its scroller reaches out over the body's right padding (-mr-6) so the scrollbar
          sits at the dialog's edge, and keeps 14 px before it, which with the 10 px bar matches the 24 px on the left.
        -->
        <xui-tabs
          renderActiveTabPanelOnly
          [animate]="false"
          [selectedTabId]="ui.settings() ?? 'preferences'"
          (selectedTabIdChange)="select($event)"
        >
          <xui-tab id="preferences" title="Preferences">
            <cth-preferences-panel
              class="-mr-6 h-[min(32rem,60vh)] [scrollbar-gutter:stable] overflow-y-auto pt-4 pr-3.5"
            />
          </xui-tab>
          <xui-tab id="setup" title="Audio setup">
            <cth-setup-page class="-mr-6 !h-[min(32rem,60vh)] [scrollbar-gutter:stable] !pt-4 !pr-3.5 !pb-0 !pl-0" />
          </xui-tab>
          <xui-tab id="shortcuts" title="Keyboard shortcuts">
            <cth-shortcut-list
              class="-mr-6 h-[min(32rem,60vh)] [scrollbar-gutter:stable] overflow-y-auto pt-4 pr-3.5"
            />
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
