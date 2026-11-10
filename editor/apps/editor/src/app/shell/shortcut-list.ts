import { ChangeDetectionStrategy, Component } from '@angular/core';
import { keyNames } from '@cathode/audio-ui';
import { XuiKbdImports } from '@xui/kbd';
import { ShortcutGroups, Shortcuts, shortcutKeys, shortcutLabel } from './shortcuts';

/** The Keyboard shortcuts tab of Settings (`?` opens it): every shortcut, grouped as the editor's commands are. */
@Component({
  selector: 'cth-shortcut-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [XuiKbdImports],
  host: { class: 'block' },
  template: `
    <div class="grid grid-cols-1 gap-x-8 gap-y-5 sm:grid-cols-2" data-testid="shortcut-help">
      @for (group of groups; track group.name) {
        <section [attr.aria-label]="group.name">
          <h3 class="section-label mb-1.5 px-1">{{ group.name }}</h3>
          <!-- A grouped list: a row per shortcut, its keys as keycaps on the right. -->
          <dl class="settings-group text-sm">
            @for (item of group.items; track item.id) {
              <div class="flex min-h-8 items-center justify-between gap-3 px-3 py-1">
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
        <h3 class="section-label mb-1.5 px-1">Knobs and grid</h3>
        <ul class="settings-group text-foreground-muted text-sm [&>li]:px-3 [&>li]:py-1.5">
          <li>
            Knobs: drag ({{ keys.shift }} fine), wheel, arrows and Page keys, {{ keys.alt }}-click resets, double-click
            types a value
          </li>
          <li>Grid: arrows move, ↵ adds a block, drag moves ({{ keys.alt }} copies)</li>
          <li>Drag a block onto another to swap them; {{ keys.mod }}-drag to replace it</li>
        </ul>
      </section>
    </div>
  `
})
export class ShortcutList {
  protected readonly keys = keyNames();
  protected readonly groups = ShortcutGroups.map(name => ({
    name,
    items: Shortcuts.filter(s => s.group === name).map(s => ({
      id: s.id,
      label: shortcutLabel(s),
      keys: shortcutKeys(s)
    }))
  })).filter(g => g.items.length > 0);
}
