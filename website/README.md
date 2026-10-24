# Cathode website

The landing page for Cathode: Angular 22 and Tailwind CSS 4, prerendered to static HTML at build time (no server). It
follows the xuijs.org landing page's design: a black stage, gradient display headlines, translucent glass cards, and
one accent colour, here the cyan of the app icon's waveform.

## Develop

```bash
pnpm install
pnpm start
```

`pnpm build` prerenders every route into `dist/website/browser`, ready for any static host; `404/index.html` is the
page for unknown paths. `pnpm format` uses the same Prettier settings as the editor and the desktop app.

## Content

- `src/app/content.ts`: every figure and list on the page (amps, blocks, platforms). Keep it in step with the README
  and `docs/07-amp-catalogue.md` and `docs/08-blocks-parity.md`.
- `src/app/screenshots.ts`: the screenshots. Save a picture as `public/screenshots/<file>` and set its `src` to
  `screenshots/<file>`; until then the page shows a placeholder that names the file it expects.
- `public/icon.png` is the desktop app's icon (`editor/apps/desktop/assets/icon.png`); `logo.png`, `favicon-64.png`
  and `apple-touch-icon.png` are made from it.
