import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CthKnob } from '@cathode/audio-ui';
import { XuiCardImports } from '@xui/card';
import { XuiSliderImports } from '@xui/slider';
import { XuiTextImports } from '@xui/text';
import { RigControl } from '../controls/rig-knob';
import { ConnectionService } from '../core/connection';
import { Notify } from '../core/notify';
import { GlobalStore } from '../state/global.store';
import { LiveStore } from '../state/live.store';

/**
 * The controllers modifiers draw on (docs/02-architecture.md §5.4): two LFOs, two ADSRs, the envelope and pitch
 * followers and the sequencer (stored with the preset), the active scene's level and controllers, and Manual A-D
 * (global). Assign them to a knob by right-clicking it on the block page.
 */
@Component({
    selector: 'cth-controllers-page',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [CthKnob, RigControl, XuiCardImports, XuiSliderImports, XuiTextImports],
    host: { class: 'block h-full overflow-auto p-6' },
    template: `
        <div class="flex max-w-6xl flex-col gap-6">
            <h1 xuiHeading class="text-lg">Controllers</h1>

            <section xuiCard class="flex flex-col gap-3 p-4" aria-labelledby="scene-heading">
                <h2 xuiHeading id="scene-heading" class="text-sm">Scene {{ live.activeScene() + 1 }}</h2>
                <div class="flex flex-wrap gap-6">
                    <cth-knob
                        label="Scene Level"
                        unit="dB"
                        [min]="-60"
                        [max]="12"
                        [defaultValue]="0"
                        [value]="scene()?.level ?? 0"
                        (valueChange)="live.updateScene(live.activeScene(), { level: $event })"
                    />
                    @for (value of sceneControllers(); track $index) {
                        <cth-knob
                            [label]="'Controller ' + ($index + 1)"
                            unit="%"
                            [min]="0"
                            [max]="100"
                            [value]="value * 100"
                            (valueChange)="setSceneController($index, $event / 100)"
                        />
                    }
                </div>
            </section>

            <section xuiCard class="flex flex-col gap-3 p-4" aria-labelledby="manual-heading">
                <h2 xuiHeading id="manual-heading" class="text-sm">Manual (global)</h2>
                <div class="flex flex-wrap gap-6">
                    @for (name of manualNames; track name; let i = $index) {
                        <cth-knob
                            [label]="'Manual ' + name"
                            unit="%"
                            [min]="0"
                            [max]="100"
                            [value]="(manual()[i] ?? 0) * 100"
                            (valueChange)="setManual(i, $event / 100)"
                        />
                    }
                </div>
            </section>

            @for (group of groups; track group.title) {
                <section xuiCard class="flex flex-col gap-3 p-4" [attr.aria-label]="group.title">
                    <h2 xuiHeading class="text-sm">{{ group.title }}</h2>
                    <div class="flex flex-wrap items-start gap-6">
                        @for (id of group.ids; track id) {
                            <cth-rig-control
                                [id]="id"
                                [label]="group.labels[$index]"
                                [stored]="stored(id)"
                                (changed)="set(id, $event)"
                            />
                        }
                    </div>
                </section>
            }

            <section xuiCard class="flex flex-col gap-3 p-4" aria-labelledby="sequencer-heading">
                <h2 xuiHeading id="sequencer-heading" class="text-sm">Sequencer</h2>
                <div class="flex flex-wrap items-start gap-6">
                    <cth-rig-control
                        id="seqStages"
                        label="Stages"
                        [stored]="stored('seqStages')"
                        (changed)="set('seqStages', $event)"
                    />
                    <cth-rig-control
                        id="seqRate"
                        label="Rate"
                        [stored]="stored('seqRate')"
                        (changed)="set('seqRate', $event)"
                    />
                    <cth-rig-control
                        id="seqTempo"
                        label="Tempo"
                        [stored]="stored('seqTempo')"
                        (changed)="set('seqTempo', $event)"
                    />
                </div>
                <div class="flex h-36 items-end gap-2" role="group" aria-label="Sequencer stages">
                    @for (stage of stages(); track stage) {
                        <xui-slider
                            orientation="vertical"
                            class="h-32"
                            [min]="0"
                            [max]="1"
                            [stepSize]="0.01"
                            [labelStepSize]="1"
                            [attr.aria-label]="'Stage ' + stage"
                            [value]="stored('seq' + stage) ?? (stage % 2 === 0 ? 0 : 1)"
                            (valueChange)="set('seq' + stage, $event)"
                        />
                    }
                </div>
            </section>
        </div>
    `
})
export class ControllersPage {
    protected readonly live = inject(LiveStore);
    private readonly globals = inject(GlobalStore);
    private readonly connection = inject(ConnectionService);
    private readonly notify = inject(Notify);
    /** Values sent but not yet confirmed, so a dragged knob does not jump back. */
    private readonly pending = signal<Record<string, number>>({});

    protected readonly manualNames = ['A', 'B', 'C', 'D'];
    protected readonly groups = [
        lfo(1),
        lfo(2),
        adsr(1),
        adsr(2),
        {
            title: 'Envelope follower',
            ids: ['envAttack', 'envRelease', 'envGain'],
            labels: ['Attack', 'Release', 'Sensitivity']
        },
        { title: 'Pitch follower', ids: ['pitchLow', 'pitchHigh'], labels: ['Low', 'High'] }
    ];
    protected readonly scene = computed(() => this.live.preset()?.scenes[this.live.activeScene()]);
    protected readonly sceneControllers = computed(() => this.scene()?.controllers ?? [0, 0, 0, 0]);
    protected readonly manual = computed(() => this.globals.settings()?.manual ?? [0, 0, 0, 0]);
    protected readonly stages = computed(() =>
        Array.from({ length: Math.round(this.stored('seqStages') ?? 8) }, (_, i) => i + 1)
    );

    protected stored(id: string): number | undefined {
        return this.pending()[id] ?? this.live.preset()?.controllers[id];
    }

    protected async set(id: string, value: number): Promise<void> {
        this.pending.update(p => ({ ...p, [id]: value }));
        await this.live.setControllers({ [id]: value });
        this.pending.update(({ [id]: _, ...rest }) => rest);
    }

    protected setSceneController(index: number, value: number): void {
        const controllers = [...this.sceneControllers()];
        controllers[index] = value;
        void this.live.updateScene(this.live.activeScene(), { controllers });
    }

    protected async setManual(index: number, value: number): Promise<void> {
        await this.notify.run(async () => {
            await this.connection.client.PUT('/api/v1/live/manual/{index}', {
                params: { path: { index } },
                body: { value }
            });
            this.globals.settings.update(s =>
                s ? { ...s, manual: s.manual.map((v, i) => (i === index ? value : v)) } : s
            );
        });
    }
}

function lfo(n: number) {
    return {
        title: `LFO ${n}`,
        ids: [`lfo${n}Rate`, `lfo${n}Shape`, `lfo${n}Tempo`],
        labels: ['Rate', 'Shape', 'Tempo']
    };
}

function adsr(n: number) {
    return {
        title: `ADSR ${n}`,
        ids: [`adsr${n}Attack`, `adsr${n}Decay`, `adsr${n}Sustain`, `adsr${n}Release`, `adsr${n}Threshold`],
        labels: ['Attack', 'Decay', 'Sustain', 'Release', 'Threshold']
    };
}
