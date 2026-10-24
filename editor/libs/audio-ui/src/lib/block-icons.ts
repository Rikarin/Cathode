/**
 * Cathode's block icons: one line drawing per block type on a 24-unit grid, stroked in `currentColor` so they take
 * the tile's colour. Register them with `provideIcons(BlockIcons)` and name them through `blockIconName`.
 */
function svg(body: string): string {
  return (
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
    `stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round">${body}</svg>`
  );
}

const dot = (x: number, y: number, r = 1) => `<circle cx="${x}" cy="${y}" r="${r}" fill="currentColor" stroke="none"/>`;

export const BlockIcons: Readonly<Record<string, string>> = {
  // I/O and routing
  cthBlockInput: svg('<path d="M3 12h10M9.5 8l4 4-4 4"/><path d="M16 5h3a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2h-3"/>'),
  cthBlockOutput: svg('<path d="M8 5H5a2 2 0 0 0-2 2v10a2 2 0 0 0 2 2h3"/><path d="M11 12h10M17.5 8l4 4-4 4"/>'),
  cthBlockMux: svg(
    '<path d="M3 5.5h3.5L12 12M3 18.5h3.5L12 12M3 12h9h8"/><path d="M17.5 9l3 3-3 3"/>' +
      dot(3, 5.5) +
      dot(3, 12) +
      dot(3, 18.5)
  ),
  cthBlockMixer: svg(
    '<path d="M6 4v16M12 4v16M18 4v16"/><rect x="4" y="13" width="4" height="3" rx="1"/>' +
      '<rect x="10" y="7" width="4" height="3" rx="1"/><rect x="16" y="10" width="4" height="3" rx="1"/>'
  ),
  cthBlockShunt: svg('<path d="M5 12h14"/>' + dot(4, 12, 2) + dot(20, 12, 2)),
  cthBlockFxLoop: svg(
    '<path d="M4 12V9a3 3 0 0 1 3-3h11M15 3l3 3-3 3"/><path d="M20 12v3a3 3 0 0 1-3 3H6M9 15l-3 3 3 3"/>'
  ),
  cthBlockFeedbackSend: svg('<path d="M3 12h11M10.5 8l4 4-4 4"/><rect x="17" y="5" width="4" height="14" rx="1"/>'),
  cthBlockFeedbackReturn: svg('<rect x="3" y="5" width="4" height="14" rx="1"/><path d="M9.5 12h11M17 8l4 4-4 4"/>'),

  // Drive and amps
  cthBlockDrive: svg('<path d="M2 12c1-4 2-6 3.5-6H8c2 0 2.5 12 4.5 12H15c2 0 2.5-12 4.5-12H22"/>'),
  cthBlockDynamicDistortion: svg(
    '<path d="M3 6c3 0 5 1.5 9 1.5S18 6 21 6" stroke-dasharray="2 2.5"/>' +
      '<path d="M2 15c1-3 2-4 3.5-4H8c2 0 2.5 8 4.5 8H15c2 0 2.5-8 4.5-8H22"/>'
  ),
  cthBlockAmp: svg(
    '<rect x="2.5" y="5" width="19" height="14" rx="2"/><path d="M2.5 9.5h19M6 13h12M6 15.5h12"/>' +
      dot(6, 7.25, 0.9) +
      dot(9, 7.25, 0.9) +
      dot(12, 7.25, 0.9) +
      dot(15, 7.25, 0.9)
  ),
  cthBlockAmpCapture: svg(
    '<rect x="2.5" y="5" width="19" height="14" rx="2"/><path d="M2.5 9.5h19"/>' +
      '<path d="M7 15.5l5-3 5 3"/>' +
      dot(6, 7.25, 0.9) +
      dot(9, 7.25, 0.9) +
      dot(12, 7.25, 0.9) +
      dot(7, 15.5, 1.3) +
      dot(12, 12.5, 1.3) +
      dot(17, 15.5, 1.3)
  ),
  cthBlockCab: svg(
    '<rect x="4" y="2.5" width="16" height="19" rx="2"/><circle cx="12" cy="13.5" r="5"/><circle cx="12" cy="13.5" r="1.5"/>' +
      dot(12, 5.75, 0.9)
  ),
  cthBlockIrPlayer: svg('<path d="M3 19h18"/><path d="M3 13h3l1.5-9 2 14 2-10 1.5 6 1.5-3.5 1.5 1.5H21"/>'),

  // Dynamics
  cthBlockCompressor: svg('<path d="M4 12h16"/><path d="M12 3v5.5M9 6l3 3 3-3M12 21v-5.5M9 18l3-3 3 3"/>'),
  cthBlockGate: svg('<path d="M2 12h5l1.5-6 2 12 2-12 2 12 1.5-6h5"/><path d="M2 4.5v15M22 4.5v15" opacity=".5"/>'),
  cthBlockMultibandCompressor: svg(
    '<path d="M3 20h18M6 17v-6M12 17v-3M18 17V9"/><path d="M4 5l2 2 2-2M10 8l2 2 2-2M16 3l2 2 2-2"/>'
  ),

  // Filters and EQ
  cthBlockFilter: svg('<path d="M3 7h8c3 0 4.5 1.5 6 5s2 8 2 8"/><path d="M3 20h18" opacity=".5"/>'),
  cthBlockWah: svg(
    '<path d="M4 18h16v2.5H4z"/><path d="M5.5 15.5L18 8.5l1.5 2.5L7 18"/>' + '<path d="M17 4.5c1.5.5 2.5 1.5 3 3"/>'
  ),
  cthBlockFormant: svg('<path d="M2 18c2 0 3-11 5-11s3 9 5 9 3-5 5-5 3 7 5 7"/>'),
  cthBlockVocoder: svg(
    '<rect x="9.5" y="3" width="5" height="10" rx="2.5"/><path d="M7 10a5 5 0 0 0 10 0M12 15v5M9 20h6"/>' +
      '<path d="M3 9v6M20.5 7.5v9" opacity=".6"/>'
  ),
  cthBlockResonator: svg(
    '<path d="M9 3v7a3 3 0 0 0 6 0V3M12 13v8"/><path d="M5.5 5.5c-1.3 1.6-1.3 4.4 0 6M18.5 5.5c1.3 1.6 1.3 4.4 0 6"/>'
  ),
  cthBlockParametricEq: svg(
    '<path d="M2 16h4c3 0 3.5-9 6-9s3 9 6 9h4"/>' + dot(12, 7, 1.6) + '<path d="M2 20h20" opacity=".5"/>'
  ),
  cthBlockGraphicEq: svg('<path d="M5 4v16M12 4v16M19 4v16" opacity=".5"/><path d="M3 14h4M10 8h4M17 11h4"/>'),
  cthBlockToneMatch: svg(
    '<path d="M2 15c3-7 6-7 9-3s6 4 11-4"/><path d="M2 18c3-7 6-7 9-3s6 4 11-4" stroke-dasharray="2 2.5" opacity=".7"/>'
  ),

  // Modulation
  cthBlockChorus: svg(
    '<path d="M2 12c2-6 4.5-6 6.5 0s4.5 6 6.5 0 4.5-6 6.5 0"/><path d="M3.5 12c2-6 4.5-6 6.5 0s4.5 6 6.5 0" opacity=".5"/>'
  ),
  cthBlockFlanger: svg('<path d="M3 17c4-9 8-9 10-4s5 5 8-6"/><path d="M17.5 6.5l3.5.5.5 3.5"/>'),
  cthBlockPhaser: svg('<circle cx="12" cy="12" r="5.5"/><path d="M12 3v18"/>'),
  cthBlockTremolo: svg('<path d="M3 11v2M6 8.5v7M9 6v12M12 8.5v7M15 11v2M18 8.5v7M21 6v12"/>'),
  cthBlockRingModulator: svg('<circle cx="12" cy="12" r="8"/><path d="M8.5 8.5l7 7M15.5 8.5l-7 7"/>'),
  cthBlockRotary: svg(
    '<circle cx="12" cy="12" r="2.5"/><path d="M12 4a8 8 0 0 1 8 8M12 20a8 8 0 0 1-8-8"/><path d="M17.5 10l2.5 2 2-2.5M6.5 14 4 12l-2 2.5"/>'
  ),
  cthBlockVibe: svg(
    '<path d="M9 18h6M10 21h4"/><path d="M12 3a6 6 0 0 0-3.5 10.9V16h7v-2.1A6 6 0 0 0 12 3z"/>' +
      '<path d="M9.5 9.5c.8-1.3 1.7-1.3 2.5 0s1.7 1.3 2.5 0"/>'
  ),

  // Time
  cthBlockDelay: svg('<path d="M4 4v16M9.5 7v10M14.5 9.5v5M19 11v2"/>'),
  cthBlockMultiDelay: svg('<path d="M3 6v12M7 9v6M11 3v18M15 8v8M19 10.5v3"/>'),
  cthBlockMegatap: svg('<path d="M3 4v16M6 6v12M9 7.5v9M12 9v6M15 10v4M18 11v2M21 11.5v1"/>'),
  cthBlockReverb: svg(
    dot(5, 19, 1.6) + '<path d="M5 13.5a5.5 5.5 0 0 1 5.5 5.5M5 9a10 10 0 0 1 10 10M5 4.5A14.5 14.5 0 0 1 19.5 19"/>'
  ),

  // Pitch and synth
  cthBlockPitch: svg(
    '<circle cx="7" cy="17" r="2.5"/><path d="M9.5 17V4l4.5 2"/><path d="M19 4v16M16.5 6.5 19 4l2.5 2.5M16.5 17.5 19 20l2.5-2.5"/>'
  ),
  cthBlockSynth: svg(
    '<rect x="3" y="5" width="18" height="14" rx="1.5"/><path d="M9 12v7M15 12v7"/>' +
      '<rect x="7.5" y="5" width="3" height="7" fill="currentColor" stroke="none"/>' +
      '<rect x="13.5" y="5" width="3" height="7" fill="currentColor" stroke="none"/>'
  ),

  // Utility
  cthBlockVolumePan: svg(
    '<path d="M4 9v6h3.5L13 19V5L7.5 9z"/><path d="M16.5 9a4 4 0 0 1 0 6M19 6.5a7.5 7.5 0 0 1 0 11"/>'
  ),
  cthBlockEnhancer: svg(
    '<path d="M11 3l1.8 5.2L18 10l-5.2 1.8L11 17l-1.8-5.2L4 10l5.2-1.8z"/><path d="M18.5 15l.8 2.2 2.2.8-2.2.8-.8 2.2-.8-2.2-2.2-.8 2.2-.8z"/>'
  ),
  cthBlockCrossover: svg('<path d="M3 7h6c3 0 4 10 8 10h4"/><path d="M3 17h4c4 0 5-10 8-10h6"/>'),
  cthBlockUtility: svg(
    '<circle cx="12" cy="13" r="6.5"/><path d="M12 13l3.5-3.5"/><path d="M12 3v1.5M4.5 6.5l1 1M19.5 6.5l-1 1" opacity=".6"/>'
  ),
  cthBlockGeneric: svg('<path d="M12 3l8 4.5v9L12 21l-8-4.5v-9z"/><path d="M4 7.5l8 4.5 8-4.5M12 12v9"/>')
};

/** Icons by block type id (the engine's built-in types). */
export const BlockTypeIcons: Readonly<Record<string, string>> = {
  input: 'cthBlockInput',
  output: 'cthBlockOutput',
  mux: 'cthBlockMux',
  mixer: 'cthBlockMixer',
  shunt: 'cthBlockShunt',
  fxloop: 'cthBlockFxLoop',
  fbsend: 'cthBlockFeedbackSend',
  fbreturn: 'cthBlockFeedbackReturn',
  drive: 'cthBlockDrive',
  dyndist: 'cthBlockDynamicDistortion',
  nam: 'cthBlockAmpCapture',
  cab: 'cthBlockCab',
  dynacab: 'cthBlockCab',
  irplayer: 'cthBlockIrPlayer',
  compressor: 'cthBlockCompressor',
  gate: 'cthBlockGate',
  mbcomp: 'cthBlockMultibandCompressor',
  filter: 'cthBlockFilter',
  wah: 'cthBlockWah',
  formant: 'cthBlockFormant',
  vocoder: 'cthBlockVocoder',
  resonator: 'cthBlockResonator',
  peq: 'cthBlockParametricEq',
  geq: 'cthBlockGraphicEq',
  tonematch: 'cthBlockToneMatch',
  chorus: 'cthBlockChorus',
  flanger: 'cthBlockFlanger',
  phaser: 'cthBlockPhaser',
  tremolo: 'cthBlockTremolo',
  ringmod: 'cthBlockRingModulator',
  rotary: 'cthBlockRotary',
  vibe: 'cthBlockVibe',
  delay: 'cthBlockDelay',
  multidelay: 'cthBlockMultiDelay',
  megatap: 'cthBlockMegatap',
  reverb: 'cthBlockReverb',
  pitch: 'cthBlockPitch',
  synth: 'cthBlockSynth',
  volpan: 'cthBlockVolumePan',
  enhancer: 'cthBlockEnhancer',
  crossover: 'cthBlockCrossover'
};

/** Icons for a type the editor has no drawing for yet, by its category. */
export const CategoryIcons: Readonly<Record<string, string>> = {
  Dynamics: 'cthBlockCompressor',
  Drive: 'cthBlockDrive',
  Amp: 'cthBlockAmp',
  Cab: 'cthBlockCab',
  Filter: 'cthBlockFilter',
  EQ: 'cthBlockParametricEq',
  Pitch: 'cthBlockPitch',
  Modulation: 'cthBlockChorus',
  Delay: 'cthBlockDelay',
  Reverb: 'cthBlockReverb',
  Synth: 'cthBlockSynth',
  Routing: 'cthBlockMux',
  Utility: 'cthBlockUtility',
  'I/O': 'cthBlockInput'
};

/** The icon for a block: its own drawing, else its category's, else a generic block. */
export function blockIconName(typeId: string | null, category: string): string {
  return (typeId ? BlockTypeIcons[typeId] : undefined) ?? CategoryIcons[category] ?? 'cthBlockGeneric';
}
