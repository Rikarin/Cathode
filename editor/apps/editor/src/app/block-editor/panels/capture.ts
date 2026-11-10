import type { AssetInfo } from '@cathode/api-client';

/**
 * Whether a capture holds more than one size of its model (a NAM A2 file: Full and Lite). The Server reads it from
 * the file and sends it as `slimmable` on NAM assets; AIDA-X models and other NAM files have one size.
 */
export function hasSizes(model: AssetInfo | undefined): boolean {
  return (model as { slimmable?: boolean | null } | undefined)?.slimmable === true;
}

/**
 * Whether the Amp NAM block leaves out the control `id` for the capture it holds (`model`; undefined with none
 * loaded): Size, unless the capture has more than one size, since it does nothing on any other.
 */
export function isHiddenCaptureParam(id: string, model: AssetInfo | undefined): boolean {
  return id === 'size' && !hasSizes(model);
}
