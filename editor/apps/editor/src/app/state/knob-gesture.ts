/** A prefix per editor session: gesture ids from two editors on one rig never meet. */
const Session = Math.random().toString(36).slice(2, 10);
let count = 0;

/**
 * One knob's gestures, for undo: the values a drag (or a typed value, a key, a reset, a turn of the wheel) sets carry
 * the same id, from the first change to the knob's `committed`, and the next change starts a new id. The Server makes
 * one undo step of a gesture however long it lasts, and a new one for the next gesture even a moment later.
 */
export class KnobGesture {
  private id: string | null = null;

  /** The id of the gesture in progress, starting one if none is. */
  current(): string {
    this.id ??= `${Session}-${(++count).toString(36)}`;
    return this.id;
  }

  /** The gesture ended (the knob's `committed`): the next change starts another. */
  end(): void {
    this.id = null;
  }
}
