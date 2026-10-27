import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { unwrap } from '@cathode/api-client';
import { formatValue } from '@cathode/audio-ui';
import { provideIcons } from '@ng-icons/core';
import { matDynamicFeedRound } from '@ng-icons/material-icons/round';
import { XuiButtonImports } from '@xui/button';
import { XuiDialogImports } from '@xui/dialog';
import { XuiInputImports } from '@xui/input';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { ConnectionService } from '../core/connection';
import { Notify } from '../core/notify';
import { LibraryStore } from '../state/library.store';
import { LiveStore } from '../state/live.store';
import { UiStore } from '../state/ui.store';
import { pickerEntries } from './picker-list';

/**
 * Parameter Batch Set: one control's current value into the same block (same id and type) of the presets picked here.
 * Presets without that block are skipped and named.
 */
@Component({
  selector: 'cth-batch-set-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [XuiButtonImports, XuiDialogImports, XuiInputImports, XuiSegmentedControlImports],
  providers: [provideIcons({ matDynamicFeedRound })],
  template: `
    <xui-dialog
      title="Set in other presets"
      icon="matDynamicFeedRound"
      class="w-[min(36rem,calc(100vw-2rem))] max-w-none"
      [open]="ui.batchSet() !== null"
      (openChange)="!$event && ui.batchSet.set(null)"
    >
      @if (ui.batchSet(); as target) {
        <xui-dialog-body class="flex flex-col gap-3">
          <p class="text-sm">
            <strong>{{ target.param.name }}</strong> = {{ format(target.value, target.param.unit) }} on
            <strong>{{ target.block.id }}</strong> ({{ target.block.type }})
          </p>
          <xui-segmented-control
            size="sm"
            aria-label="Channels"
            [options]="scopes()"
            [value]="scope()"
            (valueChange)="$event && scope.set($event)"
          />
          <input
            #queryField
            xuiInput
            type="search"
            placeholder="Search presets"
            aria-label="Search presets"
            [value]="query()"
            (input)="query.set(queryField.value)"
          />
          <ul class="flex h-72 flex-col overflow-y-auto text-sm" aria-label="Presets">
            @for (p of shown(); track p.id) {
              <li>
                <label class="hover:bg-active-overlay flex items-center gap-2 rounded px-2 py-1">
                  <input type="checkbox" [checked]="chosen().has(p.id)" (change)="toggle(p.id)" />
                  {{ p.name }}
                </label>
              </li>
            }
          </ul>
        </xui-dialog-body>
        <xui-dialog-footer align="between">
          <span class="text-foreground-muted text-xs">{{ chosen().size }} selected</span>
          <span class="flex gap-2">
            <button xuiButton variant="ghost" type="button" (click)="ui.batchSet.set(null)">Cancel</button>
            <button xuiButton type="button" [disabled]="chosen().size === 0" (click)="apply()">Set</button>
          </span>
        </xui-dialog-footer>
      }
    </xui-dialog>
  `
})
export class BatchSetDialog {
  protected readonly ui = inject(UiStore);
  private readonly library = inject(LibraryStore);
  private readonly live = inject(LiveStore);
  private readonly connection = inject(ConnectionService);
  private readonly notify = inject(Notify);

  protected readonly query = signal('');
  protected readonly chosen = signal<ReadonlySet<string>>(new Set());
  protected readonly scope = signal<'channel' | 'all'>('channel');
  protected readonly scopes = computed(() => [
    { label: `Channel ${this.ui.batchSet()?.channel ?? 'A'}`, value: 'channel' as const },
    { label: 'All channels', value: 'all' as const }
  ]);
  protected readonly shown = computed(() =>
    pickerEntries(
      this.library.presets().filter(p => p.id !== this.live.preset()?.id),
      this.query()
    )
  );

  constructor() {
    effect(() => {
      if (this.ui.batchSet()) {
        untracked(() => {
          this.chosen.set(new Set());
          this.query.set('');
          void this.library.refreshPresets();
        });
      }
    });
  }

  protected format(value: number, unit: string): string {
    return formatValue(value, unit);
  }

  protected toggle(id: string): void {
    this.chosen.update(set => {
      const next = new Set(set);
      if (!next.delete(id)) {
        next.add(id);
      }

      return next;
    });
  }

  protected async apply(): Promise<void> {
    const target = this.ui.batchSet();
    if (!target) {
      return;
    }

    const result = await this.notify.run(async () =>
      unwrap(
        await this.connection.client.POST('/api/v1/presets/batch-set', {
          body: {
            presets: [...this.chosen()],
            block: target.block.id,
            type: target.block.type,
            param: target.param.id,
            value: target.value,
            channel: this.scope() === 'channel' ? target.channel : null
          }
        })
      )
    );
    if (result) {
      const skipped = result.skipped.length > 0 ? `; ${result.skipped.length} without ${target.block.id} skipped` : '';
      this.notify.success(`Set in ${result.changed.length} preset(s)${skipped}.`);
      this.ui.batchSet.set(null);
    }
  }
}
