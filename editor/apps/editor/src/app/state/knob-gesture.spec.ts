import { KnobGesture } from './knob-gesture';

describe('KnobGesture', () => {
  it('keeps one id from the first change to the commit, and starts another for the next change', () => {
    const gesture = new KnobGesture();
    const first = gesture.current();
    expect(gesture.current()).toBe(first);

    gesture.end();
    const second = gesture.current();
    expect(second).not.toBe(first);
    expect(second.length).toBeLessThanOrEqual(64); // the Server's limit
  });

  it('gives two knobs different ids', () => {
    expect(new KnobGesture().current()).not.toBe(new KnobGesture().current());
  });
});
