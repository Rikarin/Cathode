import type { BlockTypeInfo, Preset, PresetBlock } from '@cathode/api-client';

/**
 * A short tag for a block in Block Info view: the initials of its name (words already in capitals kept whole, any
 * parenthesis dropped), or the first three letters of a one-word name, at most four characters, plus the instance
 * number past the first: "Graphic EQ" → GEQ, "Drive" (drive2) → DRV2.
 */
export function abbreviate(name: string, id = ''): string {
  const words = name
    .replace(/\(.*?\)/g, '')
    .split(/[\s/-]+/)
    .filter(Boolean);
  const tag =
    words.length === 1
      ? words[0].length <= 4 && words[0] === words[0].toUpperCase()
        ? words[0]
        : consonants(words[0])
      : words.map(w => (w.length > 1 && w === w.toUpperCase() ? w : w[0])).join('');
  const n = /\d+$/.exec(id)?.[0];
  return tag.toUpperCase().slice(0, 4) + (n && n !== '1' ? n : '');
}

/** The first letter, then the next consonants, three letters in all: Drive → DRV, Reverb → RVR, Chorus → CHR. */
function consonants(word: string): string {
  const rest = word.slice(1).replace(/[aeiou]/gi, '');
  return (word[0] + rest).slice(0, 3).padEnd(Math.min(3, word.length), word.slice(1));
}

/** One amp block of a preset: its model and which channel each scene uses. */
export interface AmpInUse {
  id: string;
  type: string;
  /** The model's name per channel used, from the block's Type choice. */
  models: { channel: string; model: string; scenes: number[] }[];
}

/** The preset's amp blocks, with the models their channels run and the scenes that use each channel. */
export function ampsInUse(preset: Preset, info: (type: string) => BlockTypeInfo | undefined): AmpInUse[] {
  return preset.blocks
    .filter(b => info(b.type)?.category === 'Amp')
    .map(block => {
      const typeParam = info(block.type)?.params.find(p => p.id === 'type');
      const byChannel = new Map<string, number[]>();
      preset.scenes.forEach((scene, index) => {
        const channel = scene.blocks[block.id]?.channel ?? 'A';
        byChannel.set(channel, [...(byChannel.get(channel) ?? []), index + 1]);
      });
      return {
        id: block.id,
        type: block.type,
        models: [...byChannel.entries()]
          .sort(([a], [b]) => a.localeCompare(b))
          .map(([channel, scenes]) => ({ channel, scenes, model: modelOf(block, channel, typeParam) }))
      };
    });
}

function modelOf(block: PresetBlock, channel: string, typeParam: BlockTypeInfo['params'][number] | undefined): string {
  if (!typeParam?.options) {
    return block.assets['model'] ? 'capture' : '';
  }

  const value = Math.round(block.channels[channel]?.['type'] ?? typeParam.default);
  return typeParam.options[value] ?? String(value);
}
