// Runs after `ng build`: copies the prerendered 404 page to 404.html for the host, and writes sitemap.xml from the
// routes Angular prerendered, so every page (the manual's chapters and blocks among them) is listed without keeping a
// list by hand. The site's address is read from SITE_URL in src/app/site.ts.

import { copyFileSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const dist = join(root, 'dist', 'website');
const browser = join(dist, 'browser');

copyFileSync(join(browser, '404', 'index.html'), join(browser, '404.html'));

const site = readFileSync(join(root, 'src', 'app', 'site.ts'), 'utf8').match(/SITE_URL = '([^']+)'/)?.[1];

if (!site) {
  throw new Error('SITE_URL not found in src/app/site.ts');
}

const routes = Object.keys(JSON.parse(readFileSync(join(dist, 'prerendered-routes.json'), 'utf8')).routes)
  .filter(route => route !== '/404')
  .sort((a, b) => (a === '/' ? -1 : b === '/' ? 1 : a.localeCompare(b)));

const escape = text => text.replace(/&/g, '&amp;').replace(/</g, '&lt;');

writeFileSync(
  join(browser, 'sitemap.xml'),
  [
    '<?xml version="1.0" encoding="UTF-8"?>',
    '<!-- Written by tools/postbuild.mjs from the prerendered routes. -->',
    '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">',
    ...routes.map(route => `  <url>\n    <loc>${escape(site + (route === '/' ? '/' : route))}</loc>\n  </url>`),
    '</urlset>',
    ''
  ].join('\n')
);

console.log(`Wrote 404.html and a sitemap of ${routes.length} pages.`);
