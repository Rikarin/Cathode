import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { XuiSwitchImports } from '@xui/switch';
import { MeterRates, PreferencesStore } from '../state/preferences.store';

/**
 * The Preferences tab of Settings: knob mode, mouse wheel, tooltips, meter refresh and the Circuit Amp page's default
 * view.
 */
@Component({
  selector: 'cth-preferences-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [XuiSegmentedControlImports, XuiSwitchImports],
  host: { class: 'block' },
  template: `
    <div class="grid grid-cols-[auto_1fr] items-center gap-x-6 gap-y-4 text-sm" data-testid="preferences">
      <span id="pref-knob">Knob mode</span>
      <xui-segmented-control
        size="sm"
        aria-labelledby="pref-knob"
        [options]="knobModes"
        [value]="prefs.knobMode()"
        (valueChange)="$event && prefs.set('knobMode', $event)"
      />
      <span id="pref-wheel">Mouse wheel</span>
      <xui-segmented-control
        size="sm"
        aria-labelledby="pref-wheel"
        [options]="wheels"
        [value]="prefs.wheel()"
        (valueChange)="$event && prefs.set('wheel', $event)"
      />
      <span id="pref-meters">Meter refresh</span>
      <xui-segmented-control
        size="sm"
        aria-labelledby="pref-meters"
        [options]="rates"
        [value]="prefs.meterRate()"
        (valueChange)="$event !== null && prefs.set('meterRate', $event)"
      />
      <span id="pref-amp">Amp controls</span>
      <xui-segmented-control
        size="sm"
        aria-labelledby="pref-amp"
        [options]="ampViews"
        [value]="prefs.ampView()"
        (valueChange)="$event && prefs.set('ampView', $event)"
      />
      <span id="pref-tooltips">Tooltips</span>
      <xui-switch
        aria-labelledby="pref-tooltips"
        [checked]="prefs.tooltips()"
        (checkedChange)="prefs.set('tooltips', $event)"
      />
    </div>
    <p class="text-foreground-muted mt-4 text-xs">
      Circular knobs follow the pointer around the knob. Authentic amp controls show only what the modelled amp has;
      Ideal shows every control, under its generic name where the amp has none.
    </p>
  `
})
export class PreferencesPanel {
  protected readonly prefs = inject(PreferencesStore);

  protected readonly knobModes = [
    { label: 'Linear', value: 'linear' as const },
    { label: 'Circular', value: 'circular' as const }
  ];
  protected readonly wheels = [
    { label: 'Normal', value: 'normal' as const },
    { label: 'Reverse', value: 'reverse' as const },
    { label: 'Off', value: 'off' as const }
  ];
  protected readonly rates = MeterRates.map(rate => ({
    label: rate === 0 ? 'Off' : `${rate} Hz`,
    value: rate
  }));
  protected readonly ampViews = [
    { label: 'Authentic', value: 'authentic' as const },
    { label: 'Ideal', value: 'ideal' as const }
  ];
}
