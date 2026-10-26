import type { Chapter } from '../types';

// The guide's chapters in reading order; each one's text is guide/<slug>.md.
export const CHAPTERS: Chapter[] = [
  {
    slug: 'introduction',
    title: 'Introduction',
    description: 'What Cathode is, what you need to run it and how this manual is organised.',
    part: 'Start here'
  },
  {
    slug: 'getting-started',
    title: 'Getting started',
    description: 'Install Cathode, connect your guitar and play your first preset in ten minutes.',
    part: 'Start here'
  },
  {
    slug: 'audio-setup',
    title: 'Audio setup',
    description:
      'Audio interfaces, sample rate, buffer size and latency, input and output levels, and what to plug into.',
    part: 'Start here'
  },
  {
    slug: 'the-editor',
    title: 'A tour of the editor',
    description: 'The navigation bar, the grid, the block drawer, the pages and where everything lives.',
    part: 'Start here'
  },
  {
    slug: 'the-grid',
    title: 'The grid',
    description: 'Adding, moving and wiring blocks, parallel paths, shunts and feedback loops.',
    part: 'Using Cathode'
  },
  {
    slug: 'presets-and-scenes',
    title: 'Presets, scenes and channels',
    description: 'Saving presets, switching sounds within a preset with scenes, and block channels A to D.',
    part: 'Using Cathode'
  },
  {
    slug: 'library',
    title: 'The Library',
    description: 'Banks, block presets, global blocks, the Factory bank, NAM amps and IR cabs, import and export.',
    part: 'Using Cathode'
  },
  {
    slug: 'modifiers',
    title: 'Controllers and modifiers',
    description: 'Moving any knob with LFOs, envelopes, envelope followers and expression pedals, and the tempo.',
    part: 'Using Cathode'
  },
  {
    slug: 'midi-and-footswitches',
    title: 'MIDI and footswitches',
    description: 'Connecting a MIDI controller, mapping footswitches and pedals, and the actions they can trigger.',
    part: 'Using Cathode'
  },
  {
    slug: 'live-performance',
    title: 'Playing live',
    description: 'The Perform page, the tuner, global mute, the looper and a gig checklist.',
    part: 'Using Cathode'
  },
  {
    slug: 'headless-server',
    title: 'The headless server',
    description: 'Running Cathode on a small Linux computer in your rig and editing it from another machine.',
    part: 'Using Cathode'
  },
  {
    slug: 'getting-your-sound',
    title: 'Getting your sound',
    description: 'A step-by-step method from an empty preset to a finished tone, and how to fix what is wrong with it.',
    part: 'Finding your sound'
  },
  {
    slug: 'gain-staging',
    title: 'Gain staging and levels',
    description: 'Input level, how hard each block is driven, clipping, and matching the loudness of presets.',
    part: 'Finding your sound'
  },
  {
    slug: 'choosing-an-amp',
    title: 'Choosing and dialling an amp',
    description: 'The amp families and what they suit, the front panel, and the expert settings explained.',
    part: 'Finding your sound'
  },
  {
    slug: 'cabs-and-mics',
    title: 'Cabs, speakers and microphones',
    description: 'Picking a speaker and cabinet, placing the microphones, mixing them, and impulse responses.',
    part: 'Finding your sound'
  },
  {
    slug: 'signal-chain',
    title: 'Building a signal chain',
    description: 'Where each kind of effect goes and why, in series, in parallel and in stereo.',
    part: 'Finding your sound'
  },
  {
    slug: 'tone-recipes',
    title: 'Tone recipes',
    description: 'Starting points for blues, classic rock, modern metal, clean, ambient, country, jazz and more.',
    part: 'Finding your sound'
  },
  {
    slug: 'nam-and-irs',
    title: 'Neural captures and impulse responses',
    description: 'Loading NAM and AIDA-X captures and your own IRs, and capturing an IR with a sine sweep.',
    part: 'Finding your sound'
  },
  {
    slug: 'keyboard-shortcuts',
    title: 'Keyboard shortcuts',
    description: 'Every keyboard shortcut in the editor.',
    part: 'Reference'
  },
  {
    slug: 'troubleshooting',
    title: 'Troubleshooting',
    description: 'No sound, crackles, latency, hum, harsh or muddy tones, and other common problems.',
    part: 'Reference'
  },
  {
    slug: 'glossary',
    title: 'Glossary',
    description: 'The words used in this manual and in amp and effects talk, explained.',
    part: 'Reference'
  }
];
