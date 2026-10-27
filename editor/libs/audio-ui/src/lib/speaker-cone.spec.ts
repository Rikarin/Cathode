import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  type ConeMic,
  type ConeMove,
  ConePlot,
  CthSpeakerCone,
  coneDescription,
  conePlacement,
  conePoint,
  coneStep
} from './speaker-cone';

const Mic: ConeMic = { slot: 0, name: 'SM57', position: 25, distance: 1, angle: 0, muted: false };

describe('speaker cone geometry', () => {
  it('maps cap to edge left to right and the grille to the top', () => {
    expect(conePoint(0, 0, 12)).toEqual({ x: ConePlot.left, y: ConePlot.top });
    expect(conePoint(100, 12, 12)).toEqual({ x: ConePlot.right, y: ConePlot.bottom });
    expect(conePoint(50, 6, 12).x).toBeCloseTo((ConePlot.left + ConePlot.right) / 2, 9);
  });

  it('inverts the mapping, clamped to the plot and rounded', () => {
    const { x, y } = conePoint(37.5, 4.25, 12);
    expect(conePlacement(x, y, 12)).toEqual({ position: 37.5, distance: 4.25 });
    expect(conePlacement(0, 1_000, 12)).toEqual({ position: 0, distance: 12 });
    expect(conePlacement(ConePlot.left + 1.234, ConePlot.top, 12).position).toBe(0.5);
  });

  it('steps with the keys and stays in range', () => {
    expect(coneStep(Mic, 'ArrowRight', false, 12)).toEqual({ position: 26, distance: 1 });
    expect(coneStep(Mic, 'ArrowLeft', true, 12)).toEqual({ position: 15, distance: 1 });
    expect(coneStep(Mic, 'ArrowUp', false, 12)).toEqual({ position: 25, distance: 0.9 });
    expect(coneStep(Mic, 'ArrowUp', true, 12)).toEqual({ position: 25, distance: 0 });
    expect(coneStep({ position: 99.5, distance: 11.95 }, 'ArrowDown', true, 12)).toEqual({
      position: 99.5,
      distance: 12
    });
    expect(coneStep(Mic, 'End', false, 12)).toEqual({ position: 100, distance: 1 });
    expect(coneStep(Mic, 'a', false, 12)).toBeNull();
  });

  it('describes a placement in words', () => {
    expect(coneDescription(Mic)).toBe('Mic 1, SM57: 25.0 % from cap to edge, 1.00 in from the grille');
    expect(coneDescription({ ...Mic, position: 0, angle: 30, muted: true })).toBe(
      'Mic 1, SM57: on the cap, 1.00 in from the grille, tilted 30°, muted'
    );
  });
});

@Component({
  imports: [CthSpeakerCone],
  template: `<cth-speaker-cone [mics]="mics" [selected]="0" (moved)="moves.push($event)" />`
})
class Host {
  readonly mics: ConeMic[] = [Mic, { ...Mic, slot: 1, name: 'R-121', position: 60, distance: 6, muted: true }];
  readonly moves: ConeMove[] = [];
}

describe('CthSpeakerCone', () => {
  it('draws each mic as a labelled slider that moves with the keys', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const handles = [...(fixture.nativeElement as HTMLElement).querySelectorAll('[role=slider]')] as SVGGElement[];

    expect(handles.length).toBe(2);
    const first = handles.find(h => h.getAttribute('aria-label') === 'Mic 1 placement');
    expect(first?.getAttribute('aria-valuetext')).toContain('SM57');
    first?.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
    expect(fixture.componentInstance.moves).toEqual([{ slot: 0, position: 26, distance: 1 }]);
  });
});
