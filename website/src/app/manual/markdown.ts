import { Marked, type Tokens } from 'marked';

/*
 * The manual's Markdown, rendered to HTML as the page is built (at prerender, so the static file carries the text).
 * Headings get anchors, tables a box that scrolls on its own, `> **Tip:**` quotes become callouts, and links to other
 * pages keep their site-root path so ProseLinks can route them inside the app.
 */

/** A heading's text as an anchor id: "Gain staging & levels" → "gain-staging-levels". */
export function slugify(text: string): string {
  return (
    text
      .toLowerCase()
      .replace(/<[^>]+>/g, '')
      .replace(/&[a-z0-9#]+;/g, '')
      .replace(/[^\p{L}\p{N}\s-]/gu, '')
      .trim()
      .replace(/\s+/g, '-')
      .replace(/-+/g, '-') || 'section'
  );
}

const escapeAttribute = (text: string) => text.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');

const CALLOUTS = ['Tip', 'Note', 'Warning'];

export interface Heading {
  readonly id: string;
  readonly text: string;
  readonly depth: number;
}

export interface Rendered {
  readonly html: string;
  /** The `##` and `###` headings, for a page's own contents. */
  readonly headings: readonly Heading[];
}

/**
 * Renders `markdown` for the page at `path` (e.g. `manual/the-grid`): heading anchors point at that page so they work
 * with the site's `<base href="/">`. Ids are kept apart from `taken` (the page's other anchors) and added to it.
 */
export function renderMarkdown(markdown: string, path: string, taken = new Set<string>()): Rendered {
  const headings: Heading[] = [];

  const marked = new Marked({
    gfm: true,
    renderer: {
      heading({ tokens, depth }: Tokens.Heading): string {
        const html = this.parser.parseInline(tokens);
        let id = slugify(html);
        for (let n = 2; taken.has(id); n++) {
          id = `${slugify(html)}-${n}`;
        }
        taken.add(id);

        if (depth === 2 || depth === 3) {
          headings.push({ id, text: html.replace(/<[^>]+>/g, ''), depth });
        }

        return (
          `<h${depth} id="${id}">${html}` +
          `<a class="anchor" href="/${path}#${id}" aria-label="Link to this section">#</a></h${depth}>\n`
        );
      },

      link({ href, title, tokens }: Tokens.Link): string {
        const text = this.parser.parseInline(tokens);
        const titleAttribute = title ? ` title="${escapeAttribute(title)}"` : '';
        // A link to another page of the site; it stays a plain path so it also works without JavaScript.
        const internal = href.startsWith('/') && !href.startsWith('//');
        const local = href.startsWith('#') ? `/${path}${href}` : href;
        const external = internal || href.startsWith('#') ? '' : ' target="_blank" rel="noreferrer noopener"';

        return `<a href="${escapeAttribute(local)}"${titleAttribute}${external}>${text}</a>`;
      },

      image({ href, title, text }: Tokens.Image): string {
        // Screenshots live in public/screenshots and are referenced as `screenshots/<file>.webp`.
        const src = href.replace(/^\.?\//, '');
        const caption = title ? `<figcaption>${title}</figcaption>` : '';
        return (
          `<figure><img src="${escapeAttribute(src)}" alt="${escapeAttribute(text)}" loading="lazy" decoding="async" />` +
          `${caption}</figure>`
        );
      },

      blockquote({ tokens }: Tokens.Blockquote): string {
        const body = this.parser.parse(tokens);
        const kind = CALLOUTS.find(name => body.startsWith(`<p><strong>${name}:</strong>`));

        return kind
          ? `<aside class="callout callout-${kind.toLowerCase()}">${body}</aside>\n`
          : `<blockquote>${body}</blockquote>\n`;
      }
    }
  });

  // Tables sit in a box of their own that scrolls sideways on a phone.
  const html = marked
    .parse(markdown, { async: false })
    .replace(/<table>/g, '<div class="table-wrap"><table>')
    .replace(/<\/table>/g, '</table></div>');

  return { html, headings };
}
