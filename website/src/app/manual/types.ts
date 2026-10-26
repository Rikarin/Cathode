// The manual's hand-written content. The knobs themselves (names, ranges, defaults, units, pages, options) are read
// from the engine into engine-params.generated.ts by `pnpm manual`, so only what a knob does is written here.

/** One block's page in the manual's block reference: `blocks/<typeId>.ts` exports one as `doc`. */
export interface BlockDoc {
  /** What the block is for, in a paragraph or two (Markdown). */
  summary: string;
  /** How to use it: where it goes in the chain, starting settings, what to try (Markdown). */
  usage?: string;
  /**
   * What each knob does, by parameter id (Markdown). A `#` in a key stands for a number of one or more digits, so
   * `tap#level` covers `tap1level` … `tap10level`; an exact id wins over a pattern.
   */
  params: Record<string, string>;
  /** What each choice of a choice parameter does: parameter id (or pattern) → option label → Markdown. */
  options?: Record<string, Record<string, string>>;
  /** Tips, common mistakes and expert notes, shown after the knobs (Markdown). */
  tips?: string;
}

/** One chapter of the guide: `guide/<slug>.md` holds its text, `guide/chapters.ts` lists these in order. */
export interface Chapter {
  /** The URL segment, /manual/<slug>, and the Markdown file's name. */
  slug: string;
  title: string;
  /** One sentence for the table of contents and the page's meta description. */
  description: string;
  /** The part of the manual it sits in, for the sidebar: 'Start here', 'Using Cathode', 'Finding your sound', … */
  part: string;
}
