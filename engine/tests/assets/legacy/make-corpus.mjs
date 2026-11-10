// Makes a release's folder of the legacy corpus (docs/03-testing-strategy.md §9, LegacyCorpusTests) through that
// release's own API, so the files are exactly what it writes.
//
// 1. Build the release: check out its tag (or the commit about to be tagged) and, in engine/,
//      dotnet build src/Cathode.Host -c Release -p:Version=<version>
//      dotnet build src/Cathode.Server -c Release -p:Version=<version>
// 2. Start its engine host with no audio device, and its Server on an empty data folder:
//      dotnet run --no-build -c Release --project src/Cathode.Host -- --serve --null-audio \
//        --socket /tmp/cathode-legacy.sock
//      dotnet run --no-build -c Release --project src/Cathode.Server -- --urls http://127.0.0.1:5700 \
//        --Cathode:DataDirectory=/tmp/cathode-legacy --Cathode:Engine:SocketPath=/tmp/cathode-legacy.sock
// 3. From the checkout that gets the folder, in engine/:
//      node tests/assets/legacy/make-corpus.mjs http://127.0.0.1:5700 /tmp/cathode-legacy <version>
// 4. Stop both, then record the new folder's levels:
//      CATHODE_UPDATE_LEGACY_LEVELS=1 dotnet test --project tests/Cathode.Server.Tests \
//        --filter-class Cathode.Server.Tests.LegacyCorpusTests
//
// It speaks the API of 1.0.0-rc.4 and later, and made the folders of rc.4 and rc.5. Those of rc.1 to rc.3 were made
// before it, through their API too, with the Amp's Cut and 5881 that schema 1 had; here the same Tiny Terror has its
// Tone on the Presence knob and a 6L6GC. 1.0.0-rc.4 has no switch settings on an engage modifier and stores that one
// without them.

import { cpSync, existsSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const [server, dataDirectory, version] = process.argv.slice(2);
if (!server || !dataDirectory || !version) {
  console.error('usage: node make-corpus.mjs <server url> <its data folder> <version>');
  process.exit(2);
}

const api = `${server.replace(/\/$/, '')}/api/v1`;

async function call(method, path, body) {
  const response = await fetch(api + path, {
    method,
    headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  if (!response.ok) {
    throw new Error(`${method} ${path}: ${response.status} ${text}`);
  }

  return text ? JSON.parse(text) : null;
}

const system = await call('GET', '/system');
if (system.version !== version) {
  throw new Error(`The Server at ${server} is ${system.version}, not ${version}.`);
}

if ((await call('GET', '/presets')).some((p) => !p.factory)) {
  throw new Error('The data folder already holds presets: start the Server on an empty one.');
}

const factory = async (name) => {
  const preset = (await call('GET', '/presets')).find((p) => p.factory && p.name === name);
  if (!preset) {
    throw new Error(`No factory preset '${name}'.`);
  }

  return preset.id;
};

// Two factory presets duplicated into the library.
for (const name of ['Comfortably Numb Solo', 'Texas Flood']) {
  const id = `legacy-${name.toLowerCase().replaceAll(' ', '-')}`;
  await call('POST', `/presets/${await factory(name)}/duplicate`, { id, name });
}

// A user's edit saved as a new preset: a Tiny Terror on channel A, another power tube on channel B, the amp made a
// global block, and two modifiers (one of them an engage modifier with its switch settings).
await call('PUT', `/live/preset/${await factory('Hideaway')}`);
await call('PATCH', '/live/blocks/circuitamp1/params?channel=A', { type: 63 });
await call('PATCH', '/live/blocks/circuitamp1/params?channel=A', { presence: 7, master: 10 });
await call('PATCH', '/live/blocks/circuitamp1/params?channel=B', { powertube: 1 });
await call('PUT', '/live/modifiers', [
  { block: 'reverb1', param: 'mix', source: 'ext1', min: 5, max: 40, curve: 'linear' },
  {
    block: 'reverb1',
    param: 'engage',
    source: 'ext2',
    min: 0,
    max: 1,
    mode: 'moving',
    threshold: 0.05,
    offDelayMs: 300,
  },
]);
await call('POST', '/live/blocks/circuitamp1/global', { name: 'Legacy Amp' });
const edits = (await call('POST', '/live/save', { name: 'Legacy Edits', asNew: true })).preset.id;

// A user block preset of a Hot Cat, and that entry exported.
await call('PUT', '/block-presets/legacy-hot-cat', {
  type: 'circuitamp',
  name: 'Legacy Hot Cat',
  values: { type: 28, presence: 3, powertube: 1, gain: 5 },
});
const exported = await call('POST', '/block-presets/export', { ids: ['legacy-hot-cat'] });

// A setlist of the user's presets.
await call('PUT', '/setlists/legacy-set', {
  name: 'Legacy Set',
  entries: [{ presetId: 'legacy-texas-flood', scene: 1, label: 'Opener' }, { presetId: edits }],
});

// A global setting changed.
const global = await call('GET', '/global');
await call('PUT', '/global', { ...global, presetSpillover: false });

const target = join(dirname(fileURLToPath(import.meta.url)), version);
rmSync(target, { recursive: true, force: true });
mkdirSync(target, { recursive: true });
for (const name of ['presets', 'block-presets', 'setlists', 'global.json']) {
  if (!existsSync(join(dataDirectory, name))) {
    throw new Error(`The Server wrote no ${name} in ${dataDirectory}.`);
  }

  cpSync(join(dataDirectory, name), join(target, name), { recursive: true });
}

writeFileSync(join(target, 'legacy-hot-cat.cthblock'), JSON.stringify(exported));
console.log(`Wrote ${target}`);
