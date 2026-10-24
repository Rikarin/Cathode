import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { type LatencyInfo, type SystemInfo, unwrap } from '@cathode/api-client';
import { XuiButtonImports } from '@xui/button';
import { XuiCalloutImports } from '@xui/callout';
import { XuiInputImports } from '@xui/input';
import { XuiSegmentedControlImports } from '@xui/segmented-control';
import { XuiSelectImports } from '@xui/select';
import { XuiStatisticImports } from '@xui/statistic';
import { XuiTextImports } from '@xui/text';
import { InputClipLevel } from '../core/clip';
import { ConnectionService } from '../core/connection';
import type { AudioDevice, AudioSettings, LatencyResult, ServerTarget } from '../core/desktop';
import { EventStream } from '../core/events';
import { Notify } from '../core/notify';
import { GlobalStore } from '../state/global.store';
import { FullScaleRange, dbuFromFullScale, fullScaleFromDbu, fullScaleFromReference } from './calibration';

const SampleRates = [44_100, 48_000, 88_200, 96_000];
const BufferSizes = [32, 64, 128, 256, 512];

/**
 * Audio setup: engine health from the Server (CPU, overloads) and where the latency goes, Input 1's clip warning and
 * calibration, and in the desktop app the interface, sample rate, buffer size, which device channels Outputs 1-4, the
 * FX loop and the DI out use, a loopback latency test, and which Server to use.
 */
@Component({
  selector: 'cth-setup-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    XuiButtonImports,
    XuiCalloutImports,
    XuiInputImports,
    XuiSegmentedControlImports,
    XuiSelectImports,
    XuiStatisticImports,
    XuiTextImports
  ],
  host: { class: 'block h-full overflow-auto p-6' },
  template: `
    <div class="flex max-w-4xl flex-col gap-8">
      <section class="flex flex-col gap-4" aria-labelledby="engine-heading">
        <h1 xuiHeading id="engine-heading" class="text-lg">Engine</h1>
        @if (system(); as s) {
          <div class="flex flex-wrap gap-10">
            <xui-statistic title="Backend" [value]="s.engine.connected ? (s.engine.backend ?? '–') : 'disconnected'" />
            <xui-statistic title="Sample rate" [value]="s.engine.sampleRate / 1000" [precision]="1" suffix=" kHz" />
            <xui-statistic title="Buffer" [value]="s.engine.bufferFrames" suffix=" frames" />
            <xui-statistic title="Latency (buffer)" [value]="bufferMs()" [precision]="2" suffix=" ms" />
            <xui-statistic title="CPU p99.9" [value]="s.engine.cpuP999" suffix=" %" />
            <xui-statistic title="CPU max" [value]="s.engine.cpuMax" [precision]="1" suffix=" %" />
            <xui-statistic title="Engine overruns" [value]="s.engine.overloads" />
            <xui-statistic title="Driver overloads" [value]="s.engine.driverOverloads" />
          </div>
          @if (s.engine.lastError) {
            <xui-callout color="error" title="Last engine error">{{ s.engine.lastError }}</xui-callout>
          }
        }
        @if (latencyReport(); as l) {
          @if (l.connected) {
            <div class="flex flex-col gap-2" data-testid="latency-report">
              <h2 xuiHeading class="text-base">Latency</h2>
              <div class="flex flex-wrap gap-10">
                <xui-statistic title="Device input" [value]="ms(l.inputFrames, l)" [precision]="2" suffix=" ms" />
                <xui-statistic title="Device output" [value]="ms(l.outputFrames, l)" [precision]="2" suffix=" ms" />
                <xui-statistic title="Buffer" [value]="ms(l.bufferFrames, l)" [precision]="2" suffix=" ms" />
                <xui-statistic title="Preset" [value]="ms(l.graphFrames, l)" [precision]="2" suffix=" ms" />
                <xui-statistic title="Total" [value]="l.totalMs" [precision]="2" suffix=" ms" />
              </div>
              <p class="text-foreground-muted text-xs">
                The device figures are what the driver reports, each including one buffer. The preset's latency is the
                longest path through the grid:
                @for (step of latentSteps(l); track step.block) {
                  {{ step.block }} {{ ms(step.frames, l).toFixed(2) }} ms{{ $last ? '.' : ',' }}
                } @empty {
                  no block on it adds any.
                }
              </p>
            </div>
          }
        }
      </section>

      <section class="flex flex-col gap-4" aria-labelledby="input-heading">
        <h2 xuiHeading id="input-heading" class="text-base">Input 1</h2>
        @if (inputClipping()) {
          <xui-callout color="error" title="Input 1 is clipping" data-testid="setup-input-clip">
            It comes within 0.5 dB of full scale. Turn the interface's gain down, then update the calibration below so
            the amps keep their drive.
          </xui-callout>
        }
        @if (input(); as i) {
          <p class="text-foreground-muted text-sm">
            Full scale: {{ i.fullScaleVolts.toFixed(3) }} V peak ({{ dbu(i.fullScaleVolts).toFixed(1) }} dBu). The amp
            and drive models read the input in volts by it; the default, 1 V, is how they always read it.
          </p>
          <div class="flex flex-wrap items-end gap-3">
            <label class="flex flex-col gap-1 text-sm">
              <span class="text-foreground-muted text-xs">Interface maximum input level</span>
              <input
                xuiInput
                type="number"
                step="0.5"
                class="w-28"
                aria-label="Interface maximum input level"
                [value]="maxLevel()"
                (input)="maxLevel.set(+$any($event.target).value)"
              />
            </label>
            <xui-segmented-control
              size="sm"
              aria-label="Unit of the maximum input level"
              [options]="unitOptions"
              [value]="unit()"
              (valueChange)="unit.set($event ?? 'dbu')"
            />
            <button xuiButton type="button" variant="outline" (click)="calibrate(levelVolts())">Use this level</button>
          </div>
          <div class="flex flex-wrap items-end gap-3">
            <label class="flex flex-col gap-1 text-sm">
              <span class="text-foreground-muted text-xs">Or play a reference sine of (V RMS)</span>
              <input
                xuiInput
                type="number"
                step="0.05"
                class="w-28"
                aria-label="Reference sine level"
                [value]="reference()"
                (input)="reference.set(+$any($event.target).value)"
              />
            </label>
            <button xuiButton type="button" variant="outline" [loading]="measuring()" (click)="measure()">
              Measure for 2 s
            </button>
            <span class="text-foreground-muted text-xs">
              Feed a steady sine of that level (a generator or a multimeter-checked DI box) into input 1 first.
            </span>
          </div>
        }
      </section>

      @if (desktop) {
        <section class="flex flex-col gap-4" aria-labelledby="audio-heading">
          <h2 xuiHeading id="audio-heading" class="text-base">Audio interface</h2>
          <div class="flex flex-wrap items-end gap-4">
            <label class="flex flex-col gap-1 text-sm">
              <span class="text-foreground-muted text-xs">Device</span>
              <xui-select
                class="w-80"
                aria-label="Audio device"
                [items]="deviceChoices()"
                [itemText]="deviceName"
                [value]="selectedDevice()"
                (valueChange)="pickDevice($event)"
              />
            </label>
            <div class="flex flex-col gap-1">
              <span class="text-foreground-muted text-xs">Sample rate</span>
              <xui-segmented-control
                size="sm"
                aria-label="Sample rate"
                [options]="rateOptions"
                [value]="settings()?.sampleRate ?? 48000"
                (valueChange)="patch({ sampleRate: $event ?? 48000 })"
              />
            </div>
            <div class="flex flex-col gap-1">
              <span class="text-foreground-muted text-xs">Buffer</span>
              <xui-segmented-control
                size="sm"
                aria-label="Buffer size"
                [options]="bufferOptions"
                [value]="settings()?.bufferFrames ?? 64"
                (valueChange)="patch({ bufferFrames: $event ?? 64 })"
              />
            </div>
            <label class="flex flex-col gap-1 text-sm">
              <span class="text-foreground-muted text-xs">Outputs 1-4 (first channel each)</span>
              <input
                xuiInput
                class="w-36"
                placeholder="1,3,0,0"
                aria-label="First device channel of Outputs 1 to 4"
                [value]="channels(settings()?.outputs)"
                (change)="patch({ outputs: parseOutputs($any($event.target).value) })"
              />
            </label>
            <label class="flex flex-col gap-1 text-sm">
              <span class="text-foreground-muted text-xs">DI out</span>
              <input
                xuiInput
                class="w-28"
                placeholder="e.g. 5,6"
                aria-label="DI out channels"
                [value]="channels(settings()?.diOut)"
                (change)="patch({ diOut: parseChannels($any($event.target).value) })"
              />
            </label>
            <label class="flex flex-col gap-1 text-sm">
              <span class="text-foreground-muted text-xs">FX loop send</span>
              <input
                xuiInput
                class="w-28"
                placeholder="e.g. 3,4"
                aria-label="FX loop send channels"
                [value]="channels(settings()?.loopOut)"
                (change)="patch({ loopOut: parseChannels($any($event.target).value) })"
              />
            </label>
            <label class="flex flex-col gap-1 text-sm">
              <span class="text-foreground-muted text-xs">FX loop return</span>
              <input
                xuiInput
                class="w-28"
                placeholder="e.g. 3,4"
                aria-label="FX loop return channels"
                [value]="channels(settings()?.loopIn)"
                (change)="patch({ loopIn: parseChannels($any($event.target).value) })"
              />
            </label>
            <button xuiButton type="button" [loading]="applying()" [disabled]="!dirty()" (click)="apply()">
              Apply and restart engine
            </button>
          </div>
          <div class="flex items-center gap-3">
            <button xuiButton type="button" variant="outline" [loading]="testing()" (click)="latencyTest()">
              Measure latency…
            </button>
            <span class="text-foreground-muted text-sm">
              Cable output 1 to input 1 first. The test plays clicks; turn monitors down.
            </span>
          </div>
          @if (latency(); as l) {
            <xui-callout
              [color]="l.ok ? 'success' : 'warning'"
              [title]="l.roundTripMs !== null ? 'Round trip: ' + l.roundTripMs.toFixed(2) + ' ms' : 'No measurement'"
            >
              <pre class="text-xs whitespace-pre-wrap">{{ l.output }}</pre>
            </xui-callout>
          }
        </section>

        <section class="flex flex-col gap-4" aria-labelledby="server-heading">
          <h2 xuiHeading id="server-heading" class="text-base">Server</h2>
          <xui-segmented-control
            size="sm"
            aria-label="Server mode"
            [options]="modeOptions"
            [value]="mode()"
            (valueChange)="mode.set($event ?? 'local')"
          />
          @if (mode() === 'remote') {
            <div class="flex flex-wrap items-end gap-3">
              <label class="flex flex-col gap-1">
                <span class="text-foreground-muted text-xs">URL</span>
                <input
                  xuiInput
                  class="w-72"
                  placeholder="http://192.168.1.20:5180"
                  [value]="remoteUrl()"
                  (input)="remoteUrl.set($any($event.target).value)"
                />
              </label>
              <label class="flex flex-col gap-1">
                <span class="text-foreground-muted text-xs">Access token</span>
                <input
                  xuiInput
                  type="password"
                  class="w-72"
                  [value]="remoteToken()"
                  (input)="remoteToken.set($any($event.target).value)"
                />
              </label>
            </div>
          }
          <div>
            <button xuiButton type="button" variant="outline" (click)="connect()">
              {{ mode() === 'remote' ? 'Connect' : 'Use this computer' }}
            </button>
          </div>
        </section>
      } @else {
        <xui-callout color="primary" title="Audio setup is in the desktop app">
          This editor is running in a browser, connected to {{ connection.connection()?.baseUrl }}. Choose the
          interface, sample rate and buffer in the Cathode desktop app, or start cathode-host with --device.
        </xui-callout>
      }
    </div>
  `
})
export class SetupPage {
  protected readonly connection = inject(ConnectionService);
  private readonly notify = inject(Notify);
  private readonly globals = inject(GlobalStore);
  private readonly events = inject(EventStream);
  protected readonly desktop = this.connection.desktop;

  protected readonly system = signal<SystemInfo | null>(null);
  protected readonly devices = signal<readonly AudioDevice[]>([]);
  protected readonly settings = signal<AudioSettings | null>(null);
  private readonly applied = signal<AudioSettings | null>(null);
  protected readonly applying = signal(false);
  protected readonly testing = signal(false);
  protected readonly latency = signal<LatencyResult | null>(null);
  protected readonly mode = signal<'local' | 'remote'>('local');
  protected readonly remoteUrl = signal('');
  protected readonly remoteToken = signal('');
  protected readonly latencyReport = signal<LatencyInfo | null>(null);
  protected readonly inputClipping = signal(false);
  protected readonly maxLevel = signal(4);
  protected readonly unit = signal<'dbu' | 'volts'>('dbu');
  protected readonly reference = signal(0.5);
  protected readonly measuring = signal(false);
  protected readonly input = computed(() => this.globals.settings()?.inputs[0] ?? null);
  protected readonly levelVolts = computed(() =>
    this.unit() === 'dbu' ? fullScaleFromDbu(this.maxLevel()) : this.maxLevel()
  );
  protected readonly unitOptions = [
    { label: 'dBu', value: 'dbu' as const },
    { label: 'V peak', value: 'volts' as const }
  ];
  protected readonly dbu = dbuFromFullScale;

  protected readonly rateOptions = SampleRates.map(r => ({ label: `${r / 1000} k`, value: r }));
  protected readonly bufferOptions = BufferSizes.map(b => ({ label: String(b), value: b }));
  protected readonly modeOptions = [
    { label: 'This computer', value: 'local' as const },
    { label: 'Remote', value: 'remote' as const }
  ];

  /** "No audio" is a real choice: the engine then runs on the null backend. */
  protected readonly deviceChoices = computed<(AudioDevice | null)[]>(() => [
    null,
    ...this.devices().filter(d => d.outputChannels > 0)
  ]);
  protected readonly selectedDevice = computed(
    () => this.devices().find(d => d.uid === this.settings()?.device) ?? null
  );
  protected readonly deviceName = (device: AudioDevice | null) =>
    device ? `${device.name} (${device.inputChannels} in / ${device.outputChannels} out)` : 'No audio (null backend)';
  protected readonly dirty = computed(() => JSON.stringify(this.settings()) !== JSON.stringify(this.applied()));
  protected readonly bufferMs = computed(() => {
    const engine = this.system()?.engine;
    return engine && engine.sampleRate > 0 ? (engine.bufferFrames / engine.sampleRate) * 1000 : 0;
  });

  constructor() {
    const poll = setInterval(() => void this.refreshSystem(), 2_000);
    // The clip warning holds for a second after the last over, so a single strum is seen.
    let lastOver = -Infinity;
    const meter = setInterval(() => {
      if ((this.events.latest?.inputPeak ?? 0) >= InputClipLevel) {
        lastOver = performance.now();
      }

      const clipping = performance.now() - lastOver < 1_000;
      if (clipping !== this.inputClipping()) {
        this.inputClipping.set(clipping);
      }
    }, 100);
    inject(DestroyRef).onDestroy(() => {
      clearInterval(poll);
      clearInterval(meter);
    });
    void this.globals.refresh();
    void this.refreshSystem();
    if (this.desktop) {
      void this.loadDesktop();
    }
  }

  protected pickDevice(device: AudioDevice | null): void {
    this.patch({
      device: device?.uid ?? null,
      sampleRate: device?.sampleRate ?? this.settings()?.sampleRate ?? 48_000
    });
  }

  /** Device channels for the FX Loop block, shown 1-based as on the interface ("3,4"; one channel is mono). */
  protected readonly channels = (list: number[] | undefined) => (list ?? []).join(',');

  /** First channels of Outputs 1-4: 0 (or empty) leaves one unmapped. */
  protected parseOutputs(text: string): number[] {
    return text
      .split(',')
      .map(c => Number.parseInt(c.trim(), 10))
      .map(c => (Number.isInteger(c) && c >= 0 && c <= 63 ? c : 0))
      .slice(0, 4);
  }

  protected ms(frames: number, report: LatencyInfo): number {
    return report.sampleRate > 0 ? (frames / report.sampleRate) * 1_000 : 0;
  }

  protected latentSteps(report: LatencyInfo) {
    return report.path.filter(s => s.frames > 0);
  }

  /** Stores Input 1's full scale (clamped to what the Server accepts). */
  protected async calibrate(volts: number | null): Promise<void> {
    if (volts === null || !Number.isFinite(volts)) {
      this.notify.error('No level to calibrate to.');
      return;
    }

    const clamped = Math.min(FullScaleRange.max, Math.max(FullScaleRange.min, volts));
    if (await this.globals.update(g => (g.inputs[0].fullScaleVolts = clamped))) {
      this.notify.success(`Input 1 full scale set to ${clamped.toFixed(3)} V.`);
    }
  }

  /** Reads the input meter for two seconds while the reference plays, then calibrates from its peak. */
  protected async measure(): Promise<void> {
    this.measuring.set(true);
    let peak = 0;
    const sample = setInterval(() => (peak = Math.max(peak, this.events.latest?.inputPeak ?? 0)), 30);
    await new Promise(resolve => setTimeout(resolve, 2_000));
    clearInterval(sample);
    this.measuring.set(false);
    if (peak >= InputClipLevel) {
      this.notify.error('The reference clips the input: turn it or the interface gain down and measure again.');
      return;
    }

    await this.calibrate(fullScaleFromReference(this.reference(), peak));
  }

  protected parseChannels(text: string): number[] {
    return text
      .split(',')
      .map(c => Number.parseInt(c.trim(), 10))
      .filter(c => Number.isInteger(c) && c >= 1 && c <= 64)
      .slice(0, 2);
  }

  protected patch(change: Partial<AudioSettings>): void {
    this.settings.update(s => (s ? { ...s, ...change } : s));
  }

  protected async apply(): Promise<void> {
    const settings = this.settings();
    if (!settings || !this.desktop) {
      return;
    }

    this.applying.set(true);
    await this.notify.run(async () => {
      await this.desktop!.audio.apply(settings);
      this.applied.set(settings);
      this.notify.success('Engine restarted with the new audio settings.');
    });
    this.applying.set(false);
  }

  protected async latencyTest(): Promise<void> {
    this.testing.set(true);
    const result = await this.notify.run(() => this.desktop!.audio.latencyTest());
    this.latency.set(result ?? null);
    this.testing.set(false);
  }

  protected async connect(): Promise<void> {
    const target: ServerTarget =
      this.mode() === 'remote'
        ? { mode: 'remote', baseUrl: this.remoteUrl().trim().replace(/\/+$/, ''), token: this.remoteToken() }
        : { mode: 'local' };
    await this.notify.run(() => this.desktop!.setTarget(target));
  }

  private async refreshSystem(): Promise<void> {
    try {
      this.system.set(unwrap(await this.connection.client.GET('/api/v1/system')));
      this.latencyReport.set(unwrap(await this.connection.client.GET('/api/v1/system/latency')));
    } catch {
      // the connection indicator in the toolbar already says the Server is unreachable
    }
  }

  private async loadDesktop(): Promise<void> {
    await this.notify.run(async () => {
      const [devices, settings, target] = await Promise.all([
        this.desktop!.audio.devices(),
        this.desktop!.audio.settings(),
        this.desktop!.target()
      ]);
      this.devices.set(devices.devices);
      this.settings.set(settings);
      this.applied.set(settings);
      this.mode.set(target.mode);
      if (target.mode === 'remote') {
        this.remoteUrl.set(target.baseUrl);
        this.remoteToken.set(target.token);
      }
    });
  }
}
