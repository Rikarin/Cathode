/**
 * Every screenshot on the site. To add one, save it as `public/screenshots/<file>` and set `src` to
 * `screenshots/<file>`; until then the page shows a placeholder of the same size naming the file it wants.
 */
export interface Shot {
  readonly file: string;
  /** `screenshots/<file>` once the picture is there; `null` shows the placeholder. */
  readonly src: string | null;
  /** The window bar's caption. */
  readonly title: string;
  /** What the picture shows, for screen readers and for the placeholder. */
  readonly alt: string;
  /** CSS aspect ratio, so the placeholder holds the picture's space. */
  readonly aspect: string;
}

const shot = (file: string, title: string, alt: string, aspect = '16 / 10'): Shot => ({
  file,
  src: null,
  title,
  alt,
  aspect
});

export const SHOTS = {
  grid: shot(
    'grid.png',
    'Cathode — Grid',
    'The main window: a preset on the grid, with the selected block open in the drawer below it.'
  ),
  amp: shot('amp.png', 'Amp', "An amp block's front panel: the amp's own controls, with the expert pages behind them."),
  cab: shot(
    'cab.png',
    'Cab',
    'The Cab block: speaker, microphone and cabinet, with the mic position over the speaker.'
  ),
  effects: shot('effects.png', 'Block picker', 'Adding a block: every block type, grouped, with a search field.'),
  perform: shot('perform.png', 'Perform', 'The Perform page: big scene buttons and the controls picked for the song.'),
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
