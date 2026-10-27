import { TestBed } from '@angular/core/testing';
import { AnnounceAfterMs, CthTuner, type TunerReading } from './tuner';

describe('CthTuner', () => {
  function render() {
    const fixture = TestBed.createComponent(CthTuner);
    fixture.componentRef.setInput('source', () => null);
    fixture.detectChanges();
    const tuner = fixture.componentInstance as unknown as {
      reading: { set(value: TunerReading | null): void };
      announce(time: number): void;
    };
    const region = fixture.nativeElement.querySelector('[aria-live]') as HTMLElement;
    const said = () => {
      fixture.detectChanges();
      return region.textContent?.trim();
    };
    return { fixture, tuner, region, said };
  }

  const a = (cents: number): TunerReading => ({ frequency: 110, note: 'A', octave: 2, cents });

  it('has one live region, and the fast-changing reading is not in it', () => {
    const { fixture, region } = render();
    expect(fixture.nativeElement.querySelectorAll('[aria-live]')).toHaveLength(1);
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
});
