import { ChangeDetectionStrategy, Component, type Signal, computed, inject, input, output } from '@angular/core';
import { BuiltInTypes, MidiActions, type SwitchAction } from '@cathode/api-client';
import { XuiSelectImports } from '@xui/select';
import { CatalogStore } from '../state/catalog.store';
import { GlobalStore } from '../state/global.store';
import { LibraryStore } from '../state/library.store';
import { LiveStore } from '../state/live.store';
import { type ActionContext, type Choice, freshAction, targetChoices, valueChoices } from './action-choices';
import { viewNames } from './footswitches';

const BuiltIns: ReadonlySet<string> = new Set(Object.values(BuiltInTypes));

/**
 * The rig as an action picker lists it (call in an injection context): the loaded preset's blocks by name, the banks
 * and their presets, the footswitch layouts, and the views of `layout` (the active layout when it gives null).
 */
export function injectActionContext(layout: () => string | null = () => null): Signal<ActionContext> {
  const live = inject(LiveStore);
  const catalog = inject(CatalogStore);
  const library = inject(LibraryStore);
  const globals = inject(GlobalStore);
  return computed<ActionContext>(() => {
    const settings = globals.settings();
    const own = layout();
    const shown =
      settings?.footswitches.find(l => l.id === own) ??
      settings?.footswitches.find(l => l.id === settings.activeLayout);
    const presets = library.presets();
    return {
      blocks: (live.preset()?.blocks ?? [])
        .filter(b => !BuiltIns.has(b.type))
        .map(b => ({ id: b.id, name: catalog.info(b.type)?.name ?? b.type })),
      banks: library.banks(),
      presetName: id => presets.find(p => p.id === id)?.name ?? id,
      layouts: settings?.footswitches ?? [],
      views: shown ? viewNames(shown) : ['Main']
    };
  });
}

/** A stored target as people read it: the block, bank or layout's name (the id when nothing has it). */
export function targetLabel(action: string, target: string | null | undefined, context: ActionContext): string {
  if (!target) {
    return '';
  }

  return targetChoices(action, context, null)?.find(c => c.value === target)?.label ?? target;
}

/**
 * Picks a footswitch or MIDI action from lists: the action, then its target by name (a block of the loaded preset, a
 * bank, a looper command, a layout) and its value as people count it (Scene 1, Channel B, a bank slot with its preset).
 * It stores what the Server reads, ids and engine numbers, as typing them did.
 */
@Component({
  selector: 'cth-action-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [XuiSelectImports],
  host: { class: 'flex min-w-0 flex-wrap gap-1' },
  template: `
    <xui-select
      class="min-w-28 flex-1"
      [aria-label]="label() + ' action'"
      [items]="actions()"
      [itemText]="text"
      [value]="selectedAction()"
      (valueChange)="pickAction($event)"
    />
    @if (targets(); as list) {
      <xui-select
        class="min-w-28 flex-1"
        [aria-label]="label() + ' target'"
        [items]="list"
        [itemText]="text"
        [value]="selectedTarget()"
        (valueChange)="$event && pick({ target: $event.value })"
      />
    }
    @if (values(); as list) {
      <xui-select
        class="min-w-24 flex-1"
        [aria-label]="label() + ' value'"
        [items]="list"
        [itemText]="text"
        [value]="selectedValue()"
        (valueChange)="$event && pick({ value: $event.value })"
      />
    }
  `
})
export class ActionPicker {
  /** The action shown; null for none. */
  readonly action = input<SwitchAction | null | undefined>(null);
  /** Whether "(none)" may be picked (a hold action, a preset's own action). */
  readonly optional = input(false);
  /** Names the lists for screen readers: "Switch 1 tap" reads "Switch 1 tap action", "… target", "… value". */
  readonly label = input.required<string>();
  /** The layout whose views a view action picks from; the active layout's when null. */
  readonly layout = input<string | null>(null);
  /** For a MIDI mapping: a new scene or presetSlot action starts with no value, so the message's value picks. */
  readonly fromMessage = input(false);
  readonly actionChange = output<SwitchAction | null>();

  protected readonly text = (choice: Choice) => choice.label;

  private readonly context = injectActionContext(() => this.layout());

  protected readonly actions = computed<Choice<string | null>[]>(() => [
    ...(this.optional() ? [{ label: '(none)', value: null }] : []),
    ...MidiActions.map(a => ({ label: a, value: a }))
  ]);
  protected readonly selectedAction = computed(
    () => this.actions().find(c => c.value === (this.action()?.action ?? null)) ?? null
  );
  protected readonly targets = computed(() => {
    const action = this.action();
    return action ? targetChoices(action.action, this.context(), action.target ?? null) : null;
  });
  protected readonly selectedTarget = computed(
    () => this.targets()?.find(c => c.value === (this.action()?.target ?? null)) ?? null
  );
  protected readonly values = computed(() => {
    const action = this.action();
    return action ? valueChoices(action.action, this.context(), action.target ?? null, action.value ?? null) : null;
  });
  protected readonly selectedValue = computed(
    () => this.values()?.find(c => c.value === (this.action()?.value ?? null)) ?? null
  );

  protected pickAction(choice: Choice<string | null> | null): void {
    const action = choice?.value ?? null;
    if (action === (this.action()?.action ?? null)) {
      return;
    }

    if (action === null) {
      if (this.optional()) {
        this.actionChange.emit(null);
      }

      return;
    }

    this.actionChange.emit(freshAction(action, this.context(), this.fromMessage()));
  }

  protected pick(change: Partial<SwitchAction>): void {
    const current = this.action();
    if (current) {
      this.actionChange.emit({
        action: current.action,
        target: current.target ?? null,
        value: current.value ?? null,
        ...change
      });
    }
  }
}
