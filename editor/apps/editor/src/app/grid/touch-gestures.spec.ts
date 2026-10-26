import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TouchGestures } from './touch-gestures';

const touch = (x = 0, y = 0) => ({ pointerType: 'touch', clientX: x, clientY: y });

describe('TouchGestures', () => {
  let now = 0;
  let doubleTaps: string[];
  let presses: [string, number, number][];
  let gestures: TouchGestures<string>;

  beforeEach(() => {
    vi.useFakeTimers();
    now = 0;
    doubleTaps = [];
    presses = [];
    gestures = new TouchGestures<string>(
      t => doubleTaps.push(t),
      (t, x, y) => presses.push([t, x, y]),
      () => now
    );
  });
  afterEach(() => vi.useRealTimers());

  it('takes two quick taps on the same target as a double tap', () => {
    gestures.down(touch(), 'a');
    gestures.cancel();
    now = 200;
    gestures.down(touch(), 'a');

    expect(doubleTaps).toEqual(['a']);
  });

  it('does not join taps on different targets or too far apart', () => {
    gestures.down(touch(), 'a');
    gestures.cancel();
    now = 100;
    gestures.down(touch(), 'b');
    gestures.cancel();
    now = 1_000;
    gestures.down(touch(), 'b');

    expect(doubleTaps).toEqual([]);
  });

  it('fires a long press where the finger stays, but not after it moves or lifts', () => {
    gestures.down(touch(40, 50), 'a');
    vi.advanceTimersByTime(600);
    gestures.down(touch(0, 0), 'b');
    gestures.move({ clientX: 30, clientY: 0 });
    vi.advanceTimersByTime(600);
    gestures.down(touch(0, 0), 'c');
    gestures.cancel();
    vi.advanceTimersByTime(600);

    expect(presses).toEqual([['a', 40, 50]]);
  });

  it('leaves a mouse alone', () => {
    gestures.down({ pointerType: 'mouse', clientX: 0, clientY: 0 }, 'a');
    gestures.down({ pointerType: 'mouse', clientX: 0, clientY: 0 }, 'a');
    vi.advanceTimersByTime(600);

    expect(doubleTaps).toEqual([]);
    expect(presses).toEqual([]);
  });
});
