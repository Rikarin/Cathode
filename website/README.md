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

## Search engines and social cards

Each page writes its own title, description, canonical link, Open Graph and Twitter card tags (and the home page its
schema.org JSON-LD) through `src/app/seo.ts`, so they are in the prerendered HTML. The site's address is `SITE_URL` in
`src/app/site.ts`; `public/robots.txt` repeats it, so change both together. `pnpm build` writes `sitemap.xml` from
every route it prerendered (`tools/postbuild.mjs`), so a new prerendered page is listed without further work. `public/social.jpg` (1200 × 630) is the card image every page shares.

## Content

- `src/app/content.ts`: every list on the home page (amp families, block groups, platforms). Keep it in step with the README
  and `docs/07-amp-catalogue.md` and `docs/08-blocks-parity.md`.
- `src/app/catalog/`: the `/catalog` page's data. `engine.generated.ts` is written by `pnpm catalog` (needs the
  .NET 10 SDK), which reads the engine's block registry: every block and its types, the drive and wah circuits with
  what each is modelled after, and the Cab block's speakers, microphones and cabinets. Run it after the engine gains a
  block, a type or a model, and commit the result. What each amp is based on is kept by hand in `amp-models.ts` from
  `docs/07-amp-catalogue.md`; a model missing there shows under "Other". The home page's counts come from here too.
- `src/app/manual/`: the manual at `/manual`. Its guide is Markdown, one `guide/<slug>.md` per chapter, listed in reading
  order in `guide/chapters.ts`; its block reference has one `blocks/<typeId>.ts` per block, which says what the block
  and each of its knobs do (`types.ts` is the shape of both). Everything else about a knob (its name, range, default,
  unit, page, choices, and its name on each Amp model) is read from the engine into `engine-params.generated.ts` by
  `pnpm manual` (needs the .NET 10 SDK); run it when a block gains or changes a parameter, and commit the result.
  The `website` workflow fails when either generated file differs from what the engine writes, when a knob has no
  description (`manual:check --strict`), when a file is not formatted, or when the prerendered build fails.
  `pnpm build` and `pnpm start` first write `content.generated.ts` (not committed), an import of every chapter and
  block page that exists, so one that is not written yet only leaves a gap; after adding a file while `pnpm start` is
  running, run `node tools/manual-index.mjs`. `pnpm manual:check` lists what is missing (blocks with no page, knobs with
  no description, chapters with no Markdown, text for knobs the engine no longer has); `--strict` makes it fail, and
  `--verbose` adds undescribed choices. In Markdown, link pages as `/manual/<slug>`, `/manual/blocks/<typeId>` and
  `/manual/blocks/<typeId>#knob-<id>`, put screenshots in as `![…](screenshots/<file>.webp)`, and start a quote with
  `**Tip:**`, `**Note:**` or `**Warning:**` for a callout.
- `src/app/screenshots.ts`: the screenshots, as WebP in `public/screenshots`. Set a shot's `src` and its `aspect`
  (width / height) when its picture arrives; until then the page shows a placeholder that names the file it expects.
  A whole macOS window capture (title bar and rounded corners, the shadow margin cropped off) is marked `window` and
  shown as it is; a crop of part of the app gets the glass frame.
- `public/icon.png` is the desktop app's icon (`editor/apps/desktop/assets/icon.png`); `logo.png`, `favicon-64.png`
  and `apple-touch-icon.png` are made from it.
