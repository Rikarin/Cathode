import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  inject,
  input,
  numberAttribute,
  output
} from '@angular/core';

/** One mic on the diagram: where it is (position 0 cap … 100 edge, %; distance in inches) and how it is drawn. */
export interface ConeMic {
  /** Slot index, 0-based. */
  readonly slot: number;
  /** Short name, e.g. "SM57". */
  readonly name: string;
  readonly position: number;
  readonly distance: number;
  /** Off-axis tilt, degrees. */
  readonly angle: number;
  readonly muted: boolean;
}

export interface ConeMove {
  readonly slot: number;
  readonly position: number;
  readonly distance: number;
}

/** The plot area inside the 360 × 252 view box: position runs left to right, distance top (grille) to bottom. */
export const ConePlot = { left: 44, right: 344, top: 70, bottom: 214 } as const;

/** Mic colours by slot, readable on the dark surface and under the slot's dark number. */
export const MicColors = ['#f5b041', '#5dade2', '#ec7063', '#58d68d'] as const;

/** Where a mic at `position` (%) and `distance` (in) sits in the view box. */
export function conePoint(position: number, distance: number, maxDistance: number): { x: number; y: number } {
  const p = Math.min(100, Math.max(0, position)) / 100;
  const d = Math.min(maxDistance, Math.max(0, distance)) / maxDistance;
  return {
    x: ConePlot.left + p * (ConePlot.right - ConePlot.left),
    y: ConePlot.top + d * (ConePlot.bottom - ConePlot.top)
  };
}

/** The mic placement under a view-box point, clamped to the plot and rounded to 0.5 % and 0.05 in. */
export function conePlacement(x: number, y: number, maxDistance: number): { position: number; distance: number } {
  const p = (x - ConePlot.left) / (ConePlot.right - ConePlot.left);
  const d = (y - ConePlot.top) / (ConePlot.bottom - ConePlot.top);
  return {
    position: Math.round(Math.min(1, Math.max(0, p)) * 200) / 2,
    distance: Math.round(Math.min(1, Math.max(0, d)) * maxDistance * 20) / 20
  };
}

/**
 * The placement after a key press on a mic: ←/→ move it across the cone by 1 % (⇧: 10 %), ↑/↓ towards or away from
 * the grille by 0.1 in (⇧: 1 in), Home/End to the cap or the edge. Null for other keys.
 */
export function coneStep(
  mic: Pick<ConeMic, 'position' | 'distance'>,
  key: string,
  shift: boolean,
  maxDistance: number
): { position: number; distance: number } | null {
  const across = shift ? 10 : 1;
  const away = shift ? 1 : 0.1;
  const clamp = (position: number, distance: number) => ({
    position: Math.min(100, Math.max(0, Math.round(position * 10) / 10)),
    distance: Math.min(maxDistance, Math.max(0, Math.round(distance * 100) / 100))
  });
  switch (key) {
    case 'ArrowLeft':
      return clamp(mic.position - across, mic.distance);
    case 'ArrowRight':
      return clamp(mic.position + across, mic.distance);
    case 'ArrowUp':
      return clamp(mic.position, mic.distance - away);
    case 'ArrowDown':
      return clamp(mic.position, mic.distance + away);
    case 'Home':
      return clamp(0, mic.distance);
    case 'End':
      return clamp(100, mic.distance);
    default:
      return null;
  }
}

/** Describes a mic's placement for screen readers and tooltips. */
export function coneDescription(mic: ConeMic): string {
  const where =
    mic.position < 0.5
      ? 'on the cap'
      : mic.position > 99.5
        ? 'at the edge'
        : `${mic.position.toFixed(1)} % from cap to edge`;
  const tilt = mic.angle > 0.5 ? `, tilted ${Math.round(mic.angle)}°` : '';
  return `Mic ${mic.slot + 1}, ${mic.name}: ${where}, ${mic.distance.toFixed(2)} in from the grille${tilt}${mic.muted ? ', muted' : ''}`;
}

/**
 * A speaker cone seen from the side, as the Axe-Fx DynaCab draws it (the Cab block's mic placement): the cone from its dust cap (left) to its edge (right) behind
 * the grille, and below it the space in front where each mic sits, across the cone horizontally and away from the
 * grille vertically. Drag a mic to move it; it is also an ARIA slider (←/→ across, ↑/↓ nearer or further, ⇧ for big
 * steps, Home/End to cap or edge). A mic's body leans by its angle; muted mics are hollow.
 */
@Component({
  selector: 'cth-speaker-cone',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block select-none' },
  template: `
    <svg
      viewBox="0 0 360 252"
      class="size-full touch-none overflow-visible"
      role="group"
      [attr.aria-label]="label()"
      (pointermove)="drag($event)"
      (pointerup)="release($event)"
      (pointercancel)="release($event)"
    >
      <defs>
        <linearGradient [attr.id]="coneId" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="#2b2f36" />
          <stop offset="100%" stop-color="#16181c" />
        </linearGradient>
        <radialGradient [attr.id]="capId" cx="30%" cy="35%" r="80%">
          <stop offset="0%" stop-color="#6b717c" />
          <stop offset="100%" stop-color="#25282e" />
        </radialGradient>
      </defs>

      <!-- The speaker: cone from the cap to the surround, the grille cloth in front of it. -->
      <g aria-hidden="true">
        <path [attr.d]="conePath" [attr.fill]="'url(#' + coneId + ')'" class="stroke-border" stroke-width="1" />
        <path [attr.d]="capPath" [attr.fill]="'url(#' + capId + ')'" stroke="rgb(255 255 255 / 0.15)" />
        <path [attr.d]="surroundPath" fill="none" class="stroke-foreground-muted" stroke-width="2" />
        <line
          [attr.x1]="plot.left - 4"
          [attr.x2]="plot.right + 8"
          [attr.y1]="plot.top - 6"
          [attr.y2]="plot.top - 6"
          class="stroke-foreground-muted"
          stroke-width="1"
          stroke-dasharray="2 3"
        />
        <text [attr.x]="plot.left" y="12" class="fill-foreground-muted text-[9px]">Cap</text>
        <text [attr.x]="plot.right" y="12" text-anchor="end" class="fill-foreground-muted text-[9px]">Edge</text>
        <text [attr.x]="plot.right + 10" [attr.y]="plot.top - 3" class="fill-foreground-muted text-[8px]">grille</text>

        @for (tick of positionTicks; track tick) {
          <line
            [attr.x1]="x(tick)"
            [attr.x2]="x(tick)"
            [attr.y1]="plot.top"
            [attr.y2]="plot.bottom"
            class="stroke-border"
            stroke-width="0.5"
          />
          <text
            [attr.x]="x(tick)"
            [attr.y]="plot.bottom + 32"
            text-anchor="middle"
            class="fill-foreground-muted text-[9px]"
          >
            {{ tick }}%
          </text>
        }
        @for (tick of distanceTicks(); track tick) {
          <line
            [attr.x1]="plot.left"
            [attr.x2]="plot.right"
            [attr.y1]="y(tick)"
            [attr.y2]="y(tick)"
            class="stroke-border"
            stroke-width="0.5"
          />
          <text
            [attr.x]="plot.left - 6"
            [attr.y]="y(tick) + 3"
            text-anchor="end"
            class="fill-foreground-muted text-[9px]"
          >
            {{ tick }}{{ tick === maxDistance() ? ' in' : '' }}
          </text>
        }
      </g>

      @for (mic of drawn(); track mic.slot) {
        <g
          role="slider"
          tabindex="0"
          class="cursor-grab focus-visible:outline-none"
          [class.opacity-60]="mic.muted"
          [attr.aria-label]="'Mic ' + (mic.slot + 1) + ' placement'"
          aria-valuemin="0"
          aria-valuemax="100"
          [attr.aria-valuenow]="mic.position"
          [attr.aria-valuetext]="mic.text"
          [attr.transform]="'translate(' + mic.x + ' ' + mic.y + ')'"
          (pointerdown)="grab($event, mic.slot)"
          (keydown)="key($event, mic)"
          (focus)="selectedChange.emit(mic.slot)"
        >
          <title>{{ mic.text }}</title>
          <line
            x1="0"
            y1="0"
            x2="0"
            [attr.y2]="plot.top - 6 - mic.y"
            [attr.stroke]="mic.color"
            stroke-width="1"
            stroke-dasharray="1 3"
          />
          <g [attr.transform]="'rotate(' + -mic.angle + ')'">
            <rect
              x="-4.5"
              y="6"
              width="9"
              height="18"
              rx="3"
              fill="#3a3e46"
              [attr.stroke]="mic.color"
              stroke-width="1"
            />
          </g>
          @if (mic.slot === selected()) {
            <circle r="15" fill="none" [attr.stroke]="mic.color" stroke-width="1.5" opacity="0.6" />
          }
          <circle
            r="10.5"
            [attr.fill]="mic.muted ? '#16181c' : mic.color"
            [attr.stroke]="mic.color"
            stroke-width="2"
            [attr.stroke-dasharray]="mic.muted ? '3 2' : null"
          />
          <text
            text-anchor="middle"
            y="4"
            class="text-[11px] font-semibold"
            [attr.fill]="mic.muted ? mic.color : '#111317'"
            aria-hidden="true"
          >
            {{ mic.slot + 1 }}
          </text>
        </g>
      }
    </svg>
  `
})
export class CthSpeakerCone {
  private static nextId = 0;
  private readonly instance = CthSpeakerCone.nextId++;
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  protected readonly coneId = `cth-cone-${this.instance}`;
  protected readonly capId = `cth-cone-cap-${this.instance}`;
  protected readonly plot = ConePlot;
  protected readonly positionTicks = [0, 25, 50, 75, 100];
  private dragging: number | null = null;

  readonly mics = input<readonly ConeMic[]>([]);
  readonly selected = input<number | null>(null);
  readonly maxDistance = input(12, { transform: numberAttribute });
  readonly label = input('Mic placement');

  /** A mic moved (drag or keys): its new position (%) and distance (in). */
  readonly moved = output<ConeMove>();
  readonly selectedChange = output<number>();

  protected readonly distanceTicks = computed(() => {
    const step = this.maxDistance() > 6 ? 2 : 1;
    return Array.from({ length: Math.floor(this.maxDistance() / step) + 1 }, (_, i) => i * step);
  });

  /** Unselected mics first, so the selected one draws (and is grabbed) on top. */
  protected readonly drawn = computed(() =>
    [...this.mics()]
      .sort((a, b) => Number(a.slot === this.selected()) - Number(b.slot === this.selected()) || b.slot - a.slot)
      .map(mic => ({
        ...mic,
        ...conePoint(mic.position, mic.distance, this.maxDistance()),
        color: MicColors[mic.slot % MicColors.length],
        text: coneDescription(mic)
      }))
  );

  // The cone in profile: deepest at the cap, rising to the surround at the baffle.
  protected readonly conePath = `M ${ConePlot.left} 22 Q ${ConePlot.left + 170} 30 ${ConePlot.right} 54 L ${ConePlot.right} 58 L ${ConePlot.left} 58 Z`;
  protected readonly capPath = `M ${ConePlot.left} 22 Q ${ConePlot.left + 26} 8 ${ConePlot.left + 52} 26 L ${ConePlot.left} 26 Z`;
  protected readonly surroundPath = `M ${ConePlot.right - 10} 55 Q ${ConePlot.right - 2} 44 ${ConePlot.right + 6} 56`;

  protected x(position: number): number {
    return conePoint(position, 0, this.maxDistance()).x;
  }

  protected y(distance: number): number {
    return conePoint(0, distance, this.maxDistance()).y;
  }

  protected grab(event: PointerEvent, slot: number): void {
    if (event.button !== 0) {
      return;
    }

    (event.currentTarget as Element).setPointerCapture?.(event.pointerId);
    this.dragging = slot;
    this.selectedChange.emit(slot);
    event.preventDefault();
  }

  protected drag(event: PointerEvent): void {
    if (this.dragging === null) {
      return;
    }

    const point = this.toViewBox(event.clientX, event.clientY);
    if (point) {
      this.moved.emit({ slot: this.dragging, ...conePlacement(point.x, point.y, this.maxDistance()) });
    }
  }

  protected release(event: PointerEvent): void {
    if (this.dragging !== null) {
      (event.target as Element).releasePointerCapture?.(event.pointerId);
      this.dragging = null;
    }
  }

  protected key(event: KeyboardEvent, mic: ConeMic): void {
    const next = coneStep(mic, event.key, event.shiftKey, this.maxDistance());
    if (next) {
      event.preventDefault();
      this.moved.emit({ slot: mic.slot, ...next });
    }
  }

  private toViewBox(clientX: number, clientY: number): { x: number; y: number } | null {
    const svg = this.host.nativeElement.querySelector('svg');
    const matrix = svg?.getScreenCTM();
    if (!svg || !matrix) {
      return null;
    }

    const point = new DOMPoint(clientX, clientY).matrixTransform(matrix.inverse());
    return { x: point.x, y: point.y };
  }
}
