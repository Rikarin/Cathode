/**
 * Every screenshot on the site. To add one, save it as `public/screenshots/<file>`, set `src` to `screenshots/<file>`
 * and `aspect` to its width / height; until then the page shows a placeholder of the same size naming the file it
 * wants. WebP, at about twice the width it is shown at.
 */
export interface Shot {
  readonly file: string;
  /** `screenshots/<file>` once the picture is there; `null` shows the placeholder. */
  readonly src: string | null;
  /** The window bar's caption. */
  readonly title: string;
  /** What the picture shows, for screen readers and for the placeholder. */
  readonly alt: string;
  /** CSS aspect ratio, so the picture (or the placeholder) holds its space before it loads. */
  readonly aspect: string;
  /**
   * A whole window as macOS captures it, with its own title bar and rounded corners (and the shadow margin cropped
   * off): shown as it is, without the glass frame and window bar, which would draw a second set. A crop of part of a
   * window leaves this off and gets the frame.
   */
  readonly window?: boolean;
}

const shot = (file: string, title: string, alt: string, aspect = '16 / 10'): Shot => ({
  file,
  src: null,
  title,
  alt,
  aspect
});

export const SHOTS = {
  grid: {
    ...shot(
      'grid.webp',
      'Cathode — Grid',
      'The main window: a preset on the grid from Input through Drive, Amp, Cab and Reverb to the Output, with the Amp open in the drawer below it.',
      '1856 / 1392'
    ),
    src: 'screenshots/grid.webp',
    window: true
  },
  amp: {
    ...shot(
      'amp.webp',
      'Amp — 5150',
      'The Amp block on the 5150 model: its own front panel of gain, tone and presence controls, the channel and type on the left, and the expert pages listed beside the knobs.',
      '1838 / 530'
    ),
    src: 'screenshots/amp.webp'
  },
  cab: {
    ...shot(
      'cab.webp',
      'Cathode — Cab',
      "The main window with the Cab block open in the drawer: four microphones placed across the speaker, from the cap to the edge and at a distance from the grille, and Mic 1's cabinet, speaker, microphone, position, distance and angle.",
      '1856 / 1392'
    ),
    src: 'screenshots/cab.webp',
    window: true
  },
  effects: {
    ...shot(
      'effects.webp',
      'Block picker',
      'Adding a block: the block picker over the grid, with a search field and the block types grouped as Filter, EQ, Pitch and Modulation, each in its own colour.',
      '1856 / 1392'
    ),
    src: 'screenshots/effects.webp',
    window: true
  },
  perform: {
    ...shot(
      'perform.webp',
      'Cathode — Perform',
      "The Perform page for this preset: the drive's Drive and Tone, the amp's Gain and Fat, the compressor's threshold, the reverb's decay and pre-delay, the gate's threshold and attack, and the cab's Mic 1 microphone and position, each as a large control.",
      '1856 / 1392'
    ),
    src: 'screenshots/perform.webp',
    window: true
  },
  modifiers: shot('modifiers.png', 'Modifier', 'A knob driven by an LFO, with the modifier dialog open.', '4 / 3'),
  library: shot(
    'library.png',
    'Library',
    'The Library: presets in banks and setlists, block presets, NAM amps and IR cabs.'
  ),
  setup: shot(
    'setup.png',
    'Audio setup',
    'Settings: the audio interface, sample rate and buffer, and the measured latency.',
    '4 / 3'
  ),
  remote: shot('remote.png', 'Phone', 'The editor in a phone browser, connected to a headless rig.', '9 / 16')
} satisfies Record<string, Shot>;
