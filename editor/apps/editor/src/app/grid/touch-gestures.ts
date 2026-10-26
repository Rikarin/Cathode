/** Taps closer in time than this, on the same cell, are a double tap. */
const DoubleTapMs = 350;
/** A finger held this long without moving is a long press. */
const LongPressMs = 550;
/** Movement (px) that makes a touch a drag or a scroll rather than a tap or a press. */
const Slop = 10;

/**
 * What a mouse does with a double click and a right click, for a finger on a tablet (an iPad in Safari, where a double
 * tap zooms and a long press fires no contextmenu): a double tap on the same target and a long press that stays put.
 * Only touch pointers count; a mouse and a pen keep their own events.
 */
export class TouchGestures<T> {
  private lastTap: { target: T; at: number } | null = null;
  private press: { x: number; y: number; timer: ReturnType<typeof setTimeout> } | null = null;

  constructor(
    private readonly onDoubleTap: (target: T) => void,
    private readonly onLongPress: (target: T, x: number, y: number) => void,
    private readonly now: () => number = () => Date.now()
  ) {}

  down(event: Pick<PointerEvent, 'pointerType' | 'clientX' | 'clientY'>, target: T): void {
    if (event.pointerType !== 'touch') {
      return;
    }

    this.cancel();
    const at = this.now();
    if (this.lastTap && this.lastTap.target === target && at - this.lastTap.at < DoubleTapMs) {
      this.lastTap = null;
      this.onDoubleTap(target);
      return;
    }

    this.lastTap = { target, at };
    const { clientX: x, clientY: y } = event;
    this.press = {
      x,
      y,
      timer: setTimeout(() => {
        this.press = null;
        this.lastTap = null;
        this.onLongPress(target, x, y);
      }, LongPressMs)
    };
  }

  move(event: Pick<PointerEvent, 'clientX' | 'clientY'>): void {
    if (this.press && Math.hypot(event.clientX - this.press.x, event.clientY - this.press.y) > Slop) {
      this.cancel();
      this.lastTap = null;
    }
  }

  /** The finger lifted (or the browser took the touch over for a scroll): no long press any more. */
  cancel(): void {
    if (this.press) {
      clearTimeout(this.press.timer);
      this.press = null;
    }
  }
}
