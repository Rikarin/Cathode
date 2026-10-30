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
      'The main window: a preset on the grid from Input through a gate, Compressor, Drive, Amp, Cab and Reverb to Output 1, with the Amp open in the drawer below it.',
      '1856 / 1410'
    ),
    src: 'screenshots/grid.webp',
    window: true
  },
  amp: {
    ...shot(
      'amp.webp',
      'Amp — 5150',
      'The Amp block on the 5150 model: its own front panel of gain, tone and presence controls, the channel and type on the left, and the expert pages listed beside the knobs.',
      '1838 / 589'
    ),
    src: 'screenshots/amp.webp'
  },
  cab: {
    ...shot(
      'cab.webp',
      'Cathode — Cab',
      "The main window with the Cab block open in the drawer: four microphones placed across the speaker, from the cap to the edge and at a distance from the grille, and Mic 1's cabinet, speaker, microphone, position, distance and angle.",
      '1856 / 1410'
    ),
    src: 'screenshots/cab.webp',
    window: true
  },
  effects: {
    ...shot(
      'effects.webp',
      'Block picker',
      "Adding a block: the block picker over the grid, a search field above one grid of every block type, from Compressor, Drive, Amp and Cab through the filters, modulation, delays and Reverb to the routing blocks and the outputs, each tile in its category's colour.",
      '1856 / 1410'
    ),
    src: 'screenshots/effects.webp',
    window: true
  },
  perform: {
    ...shot(
      'perform.webp',
      'Cathode — Perform',
      "The Perform page for this preset: the setlist bar with the current song, the next one and every song as a button, and below it the compressor's threshold, the drive's Drive and Tone, the amp's Gain, Presence and Fat, the cab's Mic 1 speaker and the reverb's size, each as a large control.",
      '1856 / 1410'
    ),
    src: 'screenshots/perform.webp',
    window: true
  },
  modifiers: {
    ...shot(
      'modifiers.webp',
      'Cathode — Modifier',
      "A modifier on the compressor's Threshold: LFO 1 as the source, its minimum, maximum, attack and release, the channels it applies on, and an exponential curve drawn on a graph.",
      '1856 / 1410'
    ),
    src: 'screenshots/modifiers.webp',
    window: true
  },
  library: {
    ...shot(
      'library.webp',
      'Cathode — Library',
      'The Library on its Presets tab: presets with favourites, colour tags, author and date, a filter for all, your own or factory presets, a search by name, author or tag, and tabs for the Block Library, global blocks, NAM amps and IR cabs.',
      '1856 / 1410'
    ),
    src: 'screenshots/library.webp',
    window: true
  },
  setup: {
    ...shot(
      'setup.webp',
      'Cathode — Audio setup',
      "Settings, Audio setup: the engine readings on an LCD strip (backend, sample rate, buffer, CPU, no overruns or overloads, and inputs), the latency of the device input and output, the buffer and the preset, 7.83 ms in all, Input 1's calibration, and the audio interface with its sample rate, buffer and outputs.",
      '1856 / 1410'
    ),
    src: 'screenshots/setup.webp',
    window: true
  },
  remote: shot('remote.png', 'Phone', 'The editor in a phone browser, connected to a headless rig.', '9 / 16')
} satisfies Record<string, Shot>;
