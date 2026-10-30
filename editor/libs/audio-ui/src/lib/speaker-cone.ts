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

/**
 * The plot area inside the 360 × 252 view box, under the speaker and the grille (y 64): position runs left to right,
 * distance top (grille) to bottom. It leaves room at the sides for a mic's ring at either end and below for its body;
 * there are no scale labels.
 */
export const ConePlot = { left: 16, right: 344, top: 70, bottom: 224 } as const;

/** Mic colours by slot, readable on the dark surface and under the slot's dark number. */
export const MicColors = ['#f5b041', '#5dade2', '#ec7063', '#58d68d'] as const;

/** The colour of a slot (0-based); the four repeat past slot 4. */
export function micColor(slot: number): string {
  return MicColors[slot % MicColors.length] ?? MicColors[0];
}

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
 * The speaker from its axis (`left`) to its edge (`right`), in view-box units above the grille (y 64): the cone's body
 * sloping from the cap to the edge (and a highlight along its top) and the dust cap's dome.
 */
export function speakerPaths(left: number, right: number) {
  return {
    cone: `M ${left} 22 Q ${left + 170} 30 ${right} 54 L ${right} 58 L ${left} 58 Z`,
    rim: `M ${left + 50} 25.5 Q ${left + 170} 30.5 ${right - 2} 54`,
    cap: `M ${left} 22 Q ${left + 26} 8 ${left + 52} 26 L ${left} 26 Z`
  };
}

/**
 * A speaker cone seen from the side, as the Axe-Fx DynaCab draws it (the Cab block's mic placement), in the theme's
 * metals: the cone from its dust cap (left) to its edge (right) behind the grille, and below it the space in front
 * where each mic sits, across the cone horizontally and away from the grille vertically. Drag a mic to move it; it is
 * also an ARIA slider (←/→ across, ↑/↓ nearer or further, ⇧ for big steps, Home/End to cap or edge). A mic's body
 * leans by its angle; muted mics are hollow.
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
        <!-- The cone dark graphite fading down, the cap a dome lit from the upper left; the mics in brushed metal. -->
        <linearGradient [attr.id]="coneId" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" style="stop-color: var(--color-surface-raised)" />
          <stop offset="100%" style="stop-color: var(--color-surface-sunken)" />
        </linearGradient>
        <radialGradient [attr.id]="capId" cx="30%" cy="35%" r="80%">
          <stop offset="0%" style="stop-color: var(--dial-top)" />
          <stop offset="100%" style="stop-color: var(--color-surface-raised)" />
        </radialGradient>
        <linearGradient [attr.id]="bodyId" x1="0" y1="0" x2="1" y2="0">
          <stop offset="0%" style="stop-color: var(--knob-bottom)" />
          <stop offset="45%" style="stop-color: var(--knob-top)" />
          <stop offset="100%" style="stop-color: var(--knob-bottom)" />
        </linearGradient>
        <radialGradient [attr.id]="glossId" cx="38%" cy="30%" r="65%">
          <stop offset="0%" stop-color="white" stop-opacity="0.55" />
          <stop offset="100%" stop-color="white" stop-opacity="0" />
        </radialGradient>
      </defs>

      <!-- The speaker: cone from the cap to the edge, the grille in front of it. -->
      <g aria-hidden="true">
        <rect
          [attr.x]="plot.left"
          [attr.y]="plot.top"
          [attr.width]="plot.right - plot.left"
          [attr.height]="plot.bottom - plot.top"
          rx="3"
          fill="rgb(0 0 0 / 0.25)"
        />
        <path
          [attr.d]="speaker.cone"
          [attr.fill]="'url(#' + coneId + ')'"
          style="stroke: var(--dial-rim)"
          stroke-width="1"
        />
        <path [attr.d]="speaker.rim" fill="none" style="stroke: var(--highlight)" stroke-width="1" />
        <path
          [attr.d]="speaker.cap"
          [attr.fill]="'url(#' + capId + ')'"
          style="stroke: var(--dial-rim)"
          stroke-width="1"
        />
        <line
          [attr.x1]="plot.left"
          [attr.x2]="plot.right"
          [attr.y1]="plot.top - 6"
          [attr.y2]="plot.top - 6"
          style="stroke: var(--well-line-strong)"
          stroke-width="1"
          stroke-dasharray="3 3"
        />
        @for (tick of positionTicks; track tick) {
          <line
            [attr.x1]="x(tick)"
            [attr.x2]="x(tick)"
            [attr.y1]="plot.top"
            [attr.y2]="plot.bottom"
            style="stroke: var(--well-line)"
            stroke-width="1"
          />
        }
        @for (tick of distanceTicks(); track tick) {
          <line
            [attr.x1]="plot.left"
            [attr.x2]="plot.right"
            [attr.y1]="y(tick)"
            [attr.y2]="y(tick)"
            style="stroke: var(--well-line)"
            stroke-width="1"
          />
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
              [attr.fill]="'url(#' + bodyId + ')'"
              stroke="rgb(0 0 0 / 0.55)"
              stroke-width="1"
            />
            <rect x="-4.5" y="20" width="9" height="1.5" [attr.fill]="mic.color" opacity="0.9" />
          </g>
          @if (mic.slot === selected()) {
            <circle r="15" fill="none" [attr.stroke]="mic.color" stroke-width="1.5" opacity="0.6" />
          }
          <circle r="11.5" fill="rgb(0 0 0 / 0.45)" cy="1.5" style="filter: blur(1.5px)" />
          <circle
            r="10.5"
            [attr.fill]="mic.muted ? 'var(--well)' : mic.color"
            [attr.stroke]="mic.muted ? mic.color : 'rgb(0 0 0 / 0.55)'"
            stroke-width="1.5"
            [attr.stroke-dasharray]="mic.muted ? '3 2' : null"
          />
          @if (!mic.muted) {
            <circle r="10.5" [attr.fill]="'url(#' + glossId + ')'" />
          }
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
  protected readonly bodyId = `cth-cone-body-${this.instance}`;
  protected readonly glossId = `cth-cone-gloss-${this.instance}`;
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
        color: micColor(mic.slot),
        text: coneDescription(mic)
      }))
  );

  // The cone in profile: deepest at the cap, rising to the surround at the baffle.
  protected readonly speaker = speakerPaths(ConePlot.left, ConePlot.right);

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

    // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition -- jsdom (the unit tests) has no pointer capture
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
      // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition -- jsdom (the unit tests) has no pointer capture
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
