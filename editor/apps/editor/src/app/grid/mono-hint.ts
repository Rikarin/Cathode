import type { PresetBlock, PresetCable } from '@cathode/api-client';

/**
 * Blocks whose output can differ between left and right (spread, ping-pong, panning, rotating speakers), so their
 * stereo is lost if a later block sums to mono.
 */
const StereoTypes = new Set([
  'chorus',
  'flanger',
  'phaser',
  'delay',
  'multidelay',
  'megatap',
  'tentap',
  'plex',
  'reverb',
  'rotary',
  'pitch',
  'enhancer',
  'tremolo',
  'crossover'
]);

/** A parameter's value in the block's active channel (or its default). */
export type ParamReader = (block: PresetBlock, param: string) => number;

/**
 * Whether a block sums its input to mono: the Circuit Amp and captured amps always (they model one channel), a Cab, IR
 * Player or Volume/Pan whose input is set to one side or the sum, and a Dynamic Distortion or Mixer in mono mode. The
 * Drive runs left and right through their own circuits, so it does not.
 */
export function sumsToMono(block: PresetBlock, param: ParamReader): boolean {
  switch (block.type) {
    case 'circuitamp':
    case 'nam':
      return true;
    case 'cab':
      return param(block, 'input') !== 3; // Left, Right or Sum L+R; 3 is Stereo
    case 'irplayer':
      return param(block, 'input') !== 0; // L+R, Left or Right; 0 is Stereo
    case 'volpan':
      return param(block, 'inputSelect') !== 0;
    case 'dyndist':
    case 'mixer':
      return param(block, 'mode') === 1;
    default:
      return false;
  }
}

function makesStereo(block: PresetBlock, param: ParamReader): boolean {
  if (block.type === 'tentap') {
    return param(block, 'mode') === 1;
  }

  return StereoTypes.has(block.type);
}

/**
 * For each block that sums to mono, the engaged stereo blocks upstream of it on the same path (following cables
 * backwards), whose width it throws away. Only blocks with such a source are in the map.
 */
export function monoHints(
  blocks: readonly PresetBlock[],
  cables: readonly PresetCable[],
  param: ParamReader,
  isBypassed: (block: PresetBlock) => boolean
): Map<string, PresetBlock[]> {
  const byId = new Map(blocks.map(b => [b.id, b]));
  const feeding = new Map<string, string[]>();
  for (const cable of cables) {
    feeding.set(cable.to, [...(feeding.get(cable.to) ?? []), cable.from]);
  }

  const hints = new Map<string, PresetBlock[]>();
  for (const block of blocks) {
    if (!sumsToMono(block, param)) {
      continue;
    }

    const sources: PresetBlock[] = [];
    const seen = new Set<string>();
    const stack = [...(feeding.get(block.id) ?? [])];
    while (stack.length > 0) {
      const id = stack.pop()!;
      const upstream = byId.get(id);
      if (!upstream || seen.has(id)) {
        continue;
      }

      seen.add(id);
      if (makesStereo(upstream, param) && !isBypassed(upstream)) {
        sources.push(upstream);
      }

      stack.push(...(feeding.get(id) ?? []));
    }

    if (sources.length > 0) {
      hints.set(
        block.id,
        sources.sort((a, b) => a.column - b.column || a.row - b.row)
      );
    }
  }

  return hints;
}

/** The marker's explanation: what is lost and where. */
export function monoHintText(sources: readonly PresetBlock[], name: (block: PresetBlock) => string): string {
  const names = sources.map(name).join(', ');
  return `Sums to mono: the stereo width of ${names} before it is lost here. Move the stereo effects after this block to keep it.`;
}
