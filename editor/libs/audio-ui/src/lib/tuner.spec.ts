import { TestBed } from '@angular/core/testing';
import { AnnounceAfterMs, CthTuner, StrobeSpeed, type TunerReading, strobeStep } from './tuner';

describe('CthTuner', () => {
  function render() {
    const fixture = TestBed.createComponent(CthTuner);
    fixture.componentRef.setInput('source', () => null);
    fixture.detectChanges();
    const tuner = fixture.componentInstance as unknown as {
      reading: { set(value: TunerReading | null): void };
      announce(time: number): void;
    };
    const region = (fixture.nativeElement as HTMLElement).querySelector('[aria-live]') as HTMLElement;
    const said = () => {
      fixture.detectChanges();
      return region.textContent.trim();
    };
    return { fixture, tuner, region, said };
  }

  const a = (cents: number): TunerReading => ({ frequency: 110, note: 'A', octave: 2, cents });

  it('has one live region, and the fast-changing reading is not in it', () => {
    const { fixture, region } = render();
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('[aria-live]')).toHaveLength(1);
    expect(region.classList).toContain('sr-only');
  });

  it('says the note once it has held, not on every reading', () => {
    const { tuner, said } = render();
    let time = 0;
    // 30 readings a second wandering around A2 for half a second: nothing is said yet.
    for (; time < AnnounceAfterMs / 2; time += 33) {
      tuner.reading.set(a(10 + (time % 7)));
      tuner.announce(time);
    }

    expect(said()).toBe('');
    for (; time < AnnounceAfterMs * 1.5; time += 33) {
      tuner.reading.set(a(10 + (time % 7)));
      tuner.announce(time);
    }

    expect(said()).toBe('A2');

    // Brought in tune: said once it holds there.
    const start = time;
    for (; time < start + AnnounceAfterMs * 1.5; time += 33) {
      tuner.reading.set(a(0.5));
      tuner.announce(time);
    }

    expect(said()).toBe('A2, in tune');
  });

  it('names the string of a tuning in what it says', () => {
    const { tuner, said } = render();
    for (let time = 0; time < AnnounceAfterMs * 1.5; time += 33) {
      tuner.reading.set({ frequency: 110, note: 'A', octave: 2, cents: 0.5, string: 5 });
      tuner.announce(time);
    }

    expect(said()).toBe('String 5, A2, in tune');
  });

  it('shows the strobe when asked, and the needle with reduced motion', () => {
    const { fixture } = render();
    const strobe = () => (fixture.nativeElement as HTMLElement).querySelector('[data-testid="tuner-strobe"]');
    expect(strobe()).toBeNull();
    fixture.componentRef.setInput('view', 'strobe');
    fixture.detectChanges();
    expect(strobe()).not.toBeNull();

    (fixture.componentInstance as unknown as { reducedMotion: { set(value: boolean): void } }).reducedMotion.set(true);
    fixture.detectChanges();
    expect(strobe()).toBeNull();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Reduced motion is on');
  });
});

describe('strobeStep', () => {
  it('stands still in tune, and moves right when sharp and left when flat, in proportion', () => {
    expect(strobeStep(5, 0, 1 / 60)).toBe(5);
    expect(strobeStep(5, 1, 0.5)).toBeCloseTo(5 + StrobeSpeed * 0.5, 9);
    expect(strobeStep(5, -1, 0.5)).toBeCloseTo(5 - StrobeSpeed * 0.5, 9);
    expect(strobeStep(5, 2, 0.5) - 5).toBeCloseTo(2 * (strobeStep(5, 1, 0.5) - 5), 9);
  });

  it('wraps within one spacing either way, and clamps a string far off to 50 cents', () => {
    expect(strobeStep(15, 1, 1, 16)).toBeCloseTo((15 + StrobeSpeed) % 16, 9);
    expect(strobeStep(0.5, -1, 1, 16)).toBeCloseTo(16 + 0.5 - StrobeSpeed, 9);
    expect(strobeStep(0, 400, 0.01, 1_000)).toBeCloseTo(50 * StrobeSpeed * 0.01, 9);
  });
});
