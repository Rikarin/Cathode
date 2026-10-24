import { ChangeDetectionStrategy, Component, model } from '@angular/core';
import { provideIcons } from '@ng-icons/core';
import { matKeyboardRound } from '@ng-icons/material-icons/round';
import { XuiDialogImports } from '@xui/dialog';
import { XuiKbdImports } from '@xui/kbd';
import { ShortcutGroups, Shortcuts, shortcutKeys } from './shortcuts';

/** The `?` overlay: every keyboard shortcut, grouped as the editor's commands are. */
@Component({
  selector: 'cth-shortcut-help',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [XuiDialogImports, XuiKbdImports],
  providers: [provideIcons({ matKeyboardRound })],
  template: `
    <xui-dialog
      title="Keyboard shortcuts"
      icon="matKeyboardRound"
      class="w-[min(56rem,calc(100vw-2rem))] max-w-none"
      [(open)]="open"
    >
      <xui-dialog-body>
        <div
          class="grid max-h-[70vh] grid-cols-1 gap-x-8 gap-y-5 overflow-y-auto sm:grid-cols-2"
          data-testid="shortcut-help"
        >
          @for (group of groups; track group.name) {
            <section [attr.aria-label]="group.name">
              <h3 class="text-foreground-muted mb-1.5 text-[11px] font-semibold tracking-wide uppercase">
                {{ group.name }}
              </h3>
              <dl class="flex flex-col gap-1 text-sm">
                @for (item of group.items; track item.id) {
                  <div class="flex items-center justify-between gap-3">
                    <dt>{{ item.label }}</dt>
                    <dd class="flex shrink-0 gap-0.5">
                      @for (key of item.keys; track $index) {
                        <kbd xuiKbd>{{ key }}</kbd>
                      }
                    </dd>
                  </div>
                }
              </dl>
            </section>
          }
          <section aria-label="Knobs and grid">
            <h3 class="text-foreground-muted mb-1.5 text-[11px] font-semibold tracking-wide uppercase">
              Knobs and grid
            </h3>
            <ul class="text-foreground-muted flex flex-col gap-1 text-sm">
              <li>Knobs: drag (⇧ fine), wheel, arrows and Page keys, ⌥-click resets, double-click types a value</li>
              <li>Grid: arrows move, ↵ adds a block, drag moves (⌥ copies)</li>
              <li>Drag a block onto another to swap them; ⌘-drag to replace it</li>
            </ul>
          </section>
        </div>
      </xui-dialog-body>
    </xui-dialog>
  `
})
export class ShortcutHelp {
  readonly open = model(false);

  protected readonly groups = ShortcutGroups.map(name => ({
    name,
    items: Shortcuts.filter(s => s.group === name).map(s => ({ id: s.id, label: s.label, keys: shortcutKeys(s) }))
  })).filter(g => g.items.length > 0);
}
