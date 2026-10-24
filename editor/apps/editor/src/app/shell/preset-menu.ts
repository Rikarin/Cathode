import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { type PresetSummary, type SnapshotSummary, unwrap } from '@cathode/api-client';
import { provideIcons } from '@ng-icons/core';
import {
  matAddRound,
  matCameraAltRound,
  matDeleteRound,
  matDeleteSweepRound,
  matExpandMoreRound,
  matHistoryRound,
  matRestoreRound,
  matViewQuiltRound
} from '@ng-icons/material-icons/round';
import { XuiAlertDialogImports } from '@xui/alert-dialog';
import { XuiButtonImports } from '@xui/button';
import { XuiDialogImports } from '@xui/dialog';
import { XuiFormFieldImports } from '@xui/form-field';
import { XuiIconImports } from '@xui/icon';
import { XuiInputImports } from '@xui/input';
import { XuiMenuImports } from '@xui/menu';
import { XuiTableImports } from '@xui/table';
import { ConnectionService } from '../core/connection';
import { Notify } from '../core/notify';
import { CompareStore } from '../state/compare.store';
import { GlobalStore } from '../state/global.store';
import { LiveStore } from '../state/live.store';
import { PresetSwitcher, needsConfirm } from '../state/preset-switcher';

/**
 * The preset commands beyond save: revert, clear, snapshots (stored on the Server, so every editor shares them) and
 * templates.
 */
@Component({
  selector: 'cth-preset-menu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    XuiAlertDialogImports,
    XuiButtonImports,
    XuiDialogImports,
    XuiFormFieldImports,
    XuiIconImports,
    XuiInputImports,
    XuiMenuImports,
    XuiTableImports
  ],
  providers: [
    provideIcons({
      matAddRound,
      matCameraAltRound,
      matDeleteRound,
      matDeleteSweepRound,
      matExpandMoreRound,
      matHistoryRound,
      matRestoreRound,
      matViewQuiltRound
    })
  ],
  host: { class: 'contents' },
  template: `
    <button xuiButton variant="ghost" size="sm" data-testid="preset-menu" [xuiMenuTriggerFor]="menu">
      Preset <ng-icon xui name="matExpandMoreRound" size="sm" />
    </button>
    <ng-template #menu>
      <xui-menu>
        <button xuiMenuItem icon="matViewQuiltRound" (click)="openTemplates()">New from template…</button>
        <button xuiMenuItem icon="matAddRound" (click)="openSaveTemplate()">Save as template…</button>
        <xui-menu-divider />
        <button xuiMenuItem icon="matCameraAltRound" data-testid="take-snapshot" (click)="snapshot()">
          Take a snapshot
        </button>
        <button xuiMenuItem icon="matHistoryRound" (click)="openSnapshots()">Snapshots…</button>
        <xui-menu-divider />
        <button xuiMenuItem icon="matRestoreRound" [disabled]="!live.state()?.dirty" (click)="revert()">
          Revert to saved
        </button>
        <button xuiMenuItem icon="matDeleteSweepRound" color="error" (click)="confirmClear.set(true)">
          Clear preset…
        </button>
        <xui-menu-divider />
        <button xuiMenuItem (click)="router.navigateByUrl('/manage')">Manage presets…</button>
      </xui-menu>
    </ng-template>

    <xui-alert-dialog
      destructive
      title="Clear the preset?"
      description="The grid and the name are emptied. Undo brings them back."
      confirmText="Clear"
      [(open)]="confirmClear"
      (confirmed)="live.clear()"
    />

    <xui-dialog
      title="Snapshots"
      icon="matHistoryRound"
      class="w-[min(36rem,calc(100vw-2rem))] max-w-none"
      [(open)]="snapshotsOpen"
    >
      <xui-dialog-body>
        @if (snapshots().length === 0) {
          <p class="text-foreground-muted py-6 text-center text-sm">No snapshots of this preset yet.</p>
        } @else {
          <xui-table>
            @for (s of snapshots(); track s.id) {
              <xui-tr>
                <xui-td class="w-full">{{ s.name }}</xui-td>
                <xui-td>
                  <button xuiButton size="sm" variant="outline" (click)="loadSnapshot(s)">Load</button>
                </xui-td>
                <xui-td>
                  <button
                    xuiButton
                    size="sm"
                    variant="ghost"
                    color="error"
                    [attr.aria-label]="'Delete snapshot ' + s.name"
                    (click)="deleteSnapshot(s)"
                  >
                    <ng-icon xui name="matDeleteRound" size="sm" />
                  </button>
                </xui-td>
              </xui-tr>
            }
          </xui-table>
        }
      </xui-dialog-body>
      <xui-dialog-footer>
        <button xuiButton variant="outline" type="button" (click)="snapshot()">Take a snapshot</button>
      </xui-dialog-footer>
    </xui-dialog>

    <xui-dialog
      title="New from template"
      icon="matViewQuiltRound"
      class="w-[min(32rem,calc(100vw-2rem))] max-w-none"
      [(open)]="templatesOpen"
    >
      <xui-dialog-body>
        @if (templates().length === 0) {
          <p class="text-foreground-muted py-6 text-center text-sm">
            No templates yet: build a starting point and choose Save as template.
          </p>
        } @else {
          <xui-table>
            @for (t of templates(); track t.id) {
              <xui-tr>
                <xui-td class="w-full">{{ t.name }}</xui-td>
                <xui-td><button xuiButton size="sm" (click)="fromTemplate(t)">New</button></xui-td>
                <xui-td>
                  <button
                    xuiButton
                    size="sm"
                    variant="ghost"
                    color="error"
                    [attr.aria-label]="'Delete template ' + t.name"
                    (click)="deleteTemplate(t)"
                  >
                    <ng-icon xui name="matDeleteRound" size="sm" />
                  </button>
                </xui-td>
              </xui-tr>
            }
          </xui-table>
        }
      </xui-dialog-body>
    </xui-dialog>

    <xui-dialog title="Save as template" icon="matAddRound" [(open)]="saveTemplateOpen">
      <xui-dialog-body>
        <xui-form-field label="Template name">
          <input
            xuiInput
            [value]="templateName()"
            (input)="templateName.set($any($event.target).value)"
            (keydown.enter)="saveTemplate()"
          />
        </xui-form-field>
      </xui-dialog-body>
      <xui-dialog-footer>
        <button xuiButton variant="ghost" type="button" (click)="saveTemplateOpen.set(false)">Cancel</button>
        <button xuiButton type="button" [disabled]="!templateName().trim()" (click)="saveTemplate()">Save</button>
      </xui-dialog-footer>
    </xui-dialog>
  `
})
export class PresetMenu {
  protected readonly live = inject(LiveStore);
  protected readonly router = inject(Router);
  private readonly compare = inject(CompareStore);
  private readonly switcher = inject(PresetSwitcher);
  private readonly globals = inject(GlobalStore);
  private readonly connection = inject(ConnectionService);
  private readonly notify = inject(Notify);

  protected readonly confirmClear = signal(false);
  protected readonly snapshotsOpen = signal(false);
  protected readonly templatesOpen = signal(false);
  protected readonly saveTemplateOpen = signal(false);
  protected readonly templateName = signal('');
  protected readonly snapshots = signal<readonly SnapshotSummary[]>([]);
  protected readonly templates = signal<readonly PresetSummary[]>([]);

  protected async revert(): Promise<void> {
    if (await this.live.revert()) {
      this.compare.reset();
    }
  }

  /** One click: the working copy as it is now, unsaved edits included, timestamped. */
  async snapshot(): Promise<void> {
    const taken = await this.notify.run(async () =>
      unwrap(await this.connection.client.POST('/api/v1/live/snapshots', { body: { name: null } }))
    );
    if (taken) {
      this.notify.success(`Snapshot “${taken.name}” taken.`);
      await this.refreshSnapshots();
    }
  }

  protected async openSnapshots(): Promise<void> {
    await this.refreshSnapshots();
    this.snapshotsOpen.set(true);
  }

  protected async loadSnapshot(snapshot: SnapshotSummary): Promise<void> {
    if (await this.live.loadSnapshot(snapshot.id)) {
      this.snapshotsOpen.set(false);
    }
  }

  protected async deleteSnapshot(snapshot: SnapshotSummary): Promise<void> {
    await this.notify.run(async () => {
      await this.connection.client.DELETE('/api/v1/snapshots/{id}', { params: { path: { id: snapshot.id } } });
    });
    await this.refreshSnapshots();
  }

  protected async openTemplates(): Promise<void> {
    await this.refreshTemplates();
    this.templatesOpen.set(true);
  }

  protected openSaveTemplate(): void {
    this.templateName.set(this.live.preset()?.name ?? '');
    this.saveTemplateOpen.set(true);
  }

  protected async saveTemplate(): Promise<void> {
    const name = this.templateName().trim();
    const saved =
      name &&
      (await this.notify.run(async () =>
        unwrap(await this.connection.client.POST('/api/v1/live/templates', { body: { name } }))
      ));
    if (saved) {
      this.saveTemplateOpen.set(false);
      this.notify.success(`Template “${saved.name}” saved.`);
    }
  }

  protected async fromTemplate(template: PresetSummary): Promise<void> {
    // Starting from a template discards the working copy, as loading a preset does: ask the same way.
    const current = this.live.preset();
    if (
      needsConfirm(this.live.state()?.dirty, this.globals.settings()?.confirmPresetChange) &&
      !this.switcher.confirm(
        `“${current?.name || 'This preset'}” has unsaved changes. Discard them and start from “${template.name}”?`
      )
    ) {
      return;
    }

    if (await this.live.newFromTemplate(template.id)) {
      this.compare.reset();
      this.templatesOpen.set(false);
    }
  }

  protected async deleteTemplate(template: PresetSummary): Promise<void> {
    await this.notify.run(async () => {
      await this.connection.client.DELETE('/api/v1/templates/{id}', { params: { path: { id: template.id } } });
    });
    await this.refreshTemplates();
  }

  private async refreshSnapshots(): Promise<void> {
    const preset = this.live.preset()?.id;
    await this.notify.run(async () =>
      this.snapshots.set(
        unwrap(await this.connection.client.GET('/api/v1/snapshots', { params: { query: { preset } } }))
      )
    );
  }

  private async refreshTemplates(): Promise<void> {
    await this.notify.run(async () =>
      this.templates.set(unwrap(await this.connection.client.GET('/api/v1/templates')))
    );
  }
}
