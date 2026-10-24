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

## Deploy (Cloudflare Workers)

`wrangler.jsonc` deploys the prerendered pages as a Worker with static assets only (no Worker code). Unknown paths get
`404.html` with a real 404 status.

To connect the repository from the Cloudflare dashboard: **Workers & Pages → Create → Import a repository**, pick the
repository, then set

| Setting        | Value                 |
| -------------- | --------------------- |
| Root directory | `website`             |
| Build command  | `pnpm build`          |
| Deploy command | `npx wrangler deploy` |

The project name in the dashboard must match `name` in `wrangler.jsonc` (`cathode-website`). Every push to the
production branch then builds and deploys; other branches get preview URLs.

From a terminal instead: `pnpm exec wrangler login` once, then `pnpm deploy`. `pnpm preview` serves the build locally
the way Cloudflare does.

## Content

- `src/app/content.ts`: every figure and list on the page (amps, blocks, platforms). Keep it in step with the README
  and `docs/07-amp-catalogue.md` and `docs/08-blocks-parity.md`.
- `src/app/screenshots.ts`: the screenshots. Save a picture as `public/screenshots/<file>` and set its `src` to
  `screenshots/<file>`; until then the page shows a placeholder that names the file it expects.
- `public/icon.png` is the desktop app's icon (`editor/apps/desktop/assets/icon.png`); `logo.png`, `favicon-64.png`
  and `apple-touch-icon.png` are made from it.
