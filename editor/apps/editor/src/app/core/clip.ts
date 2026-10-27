import type { Telemetry } from '@cathode/api-client';

/** An input within 0.5 dB of full scale is about to clip the converter. */
export const InputClipLevel = 10 ** (-0.5 / 20);

/** An output at −1 dBFS or above leaves no room for inter-sample peaks on the way out. */
export const OutputClipLevel = 10 ** (-1 / 20);

/** Which outputs (1-4) peaked at or above {@link OutputClipLevel} in a telemetry frame. */
export function clippedOutputs(frame: Telemetry | null | undefined): number[] {
  if (!frame) {
    return [];
  }

  const [first, ...rest] = frame.outputPeaks ?? [];
  const peaks =
    first === undefined
      ? [Math.max(frame.outputPeakLeft, frame.outputPeakRight)]
      : [Math.max(frame.outputPeakLeft, frame.outputPeakRight, first), ...rest];
  return peaks.flatMap((peak, i) => (peak >= OutputClipLevel ? [i + 1] : []));
}

/**
 * A warning that stays lit for a while after the last over, so a single peak is seen: {@link update} with each
 * reading returns whether to show it.
 */
export class ClipHold {
  private until = -Infinity;

  constructor(private readonly holdMs = 1_500) {}

  update(over: boolean, now: number): boolean {
    if (over) {
      this.until = now + this.holdMs;
    }

    return now < this.until;
  }
}
