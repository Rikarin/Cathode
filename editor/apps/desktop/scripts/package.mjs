// Packages the desktop app for this machine's OS into dist/release: a .dmg and .zip on macOS, an NSIS installer on
// Windows, an AppImage on Linux. Run it through `pnpm dist`, which first builds the app (nx build desktop) and the
// engine (apps/desktop/scripts/publish-engine.mjs).
//
// Signing is used when electron-builder's usual variables are set (CSC_LINK and CSC_KEY_PASSWORD; on macOS also
// APPLE_ID, APPLE_APP_SPECIFIC_PASSWORD and APPLE_TEAM_ID to notarise). Without them macOS builds are signed ad hoc, so
// they run on the machine that built them; elsewhere Gatekeeper asks the user to allow them. CATHODE_VERSION overrides
// the version (CI passes the release tag).
import { build } from 'electron-builder';
import { cpSync, existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { join, resolve } from 'node:path';

const workspace = resolve(import.meta.dirname, '../../..');
const dist = join(workspace, 'dist');
const stage = join(dist, 'apps/desktop-package');
const engine = join(dist, 'engine');
const licences = join(dist, 'apps/desktop-licenses');
const repository = resolve(workspace, '..');
const require = createRequire(import.meta.url);

for (const [path, hint] of [
  [join(dist, 'apps/desktop/main.js'), 'pnpm exec nx build desktop'],
  [join(dist, 'apps/desktop/meta.json'), 'pnpm exec nx build desktop'],
  [join(dist, 'apps/editor/browser/index.html'), 'pnpm exec nx build editor'],
  [join(dist, 'apps/editor/3rdpartylicenses.txt'), 'pnpm exec nx build editor (the production configuration)'],
  [join(engine, 'licenses'), 'node apps/desktop/scripts/publish-engine.mjs']
]) {
  if (!existsSync(path)) {
    throw new Error(`${path} is missing: run ${hint} first (pnpm dist does both).`);
  }
}

const workspacePackage = JSON.parse(readFileSync(join(workspace, 'package.json'), 'utf8'));
const version = process.env['CATHODE_VERSION']?.replace(/^v/, '') || workspacePackage.version;

// The app folder: the bundled main and preload (no node_modules: esbuild inlined everything but electron), the icon,
// and the editor build in editor/, where main.ts looks for it when packaged.
rmSync(stage, { recursive: true, force: true });
mkdirSync(stage, { recursive: true });
for (const file of ['main.js', 'preload.js', 'icon.png']) {
  cpSync(join(dist, 'apps/desktop', file), join(stage, file));
}
cpSync(join(dist, 'apps/editor/browser'), join(stage, 'editor'), { recursive: true });
writeFileSync(
  join(stage, 'package.json'),
  JSON.stringify(
    {
      name: 'cathode',
      productName: 'Cathode',
      version,
      description: 'A software guitar processor',
      license: workspacePackage.license,
      author: 'Cathode',
      main: 'main.js'
    },
    null,
    2
  )
);

// The licenses folder beside the engine in the app's resources (Help ▸ Open Source Licences opens it): Cathode's own
// licence, the notices file, the licences of the libraries in the editor's bundle as the Angular build collected them,
// and those the engine's publish gathered (.NET and the native code). Electron's and Chromium's come with Electron:
// electron-builder leaves them beside the executable on Windows and Linux and deletes them on macOS, where afterExtract
// below keeps them in this folder.
rmSync(licences, { recursive: true, force: true });
mkdirSync(licences, { recursive: true });
const notices = readFileSync(join(repository, 'THIRD-PARTY-NOTICES.md'), 'utf8');
cpSync(join(repository, 'LICENSE'), join(licences, 'LICENSE.txt'));
cpSync(join(repository, 'THIRD-PARTY-NOTICES.md'), join(licences, 'THIRD-PARTY-NOTICES.md'));
cpSync(join(dist, 'apps/editor/3rdpartylicenses.txt'), join(licences, 'editor-3rdpartylicenses.txt'));
cpSync(join(engine, 'licenses'), licences, { recursive: true });

// esbuild inlines main's npm dependencies into main.js, where no tool collects their licences: each must be named in
// the notices file, so a new dependency cannot ship without its notice.
const inlined = new Set(
  Object.keys(JSON.parse(readFileSync(join(dist, 'apps/desktop/meta.json'), 'utf8')).inputs)
    .map(input => /.*node_modules\/((?:@[^/]+\/)?[^/]+)\//.exec(input)?.[1])
    .filter(Boolean)
);
const unlisted = [...inlined].filter(name => !notices.includes(`| ${name} |`));
if (unlisted.length > 0) {
  throw new Error(
    `The desktop app's main bundle contains ${unlisted.join(', ')}, which THIRD-PARTY-NOTICES.md does not list. ` +
      'Add a row with its licence (and its copyright notice if the package ships no licence file).'
  );
}

// CI passes unset secrets as empty strings, and electron-builder reads an empty CSC_LINK as a certificate path (the
// working directory): unset them so an unsigned build stays unsigned.
for (const name of [
  'CSC_LINK',
  'CSC_KEY_PASSWORD',
  'CSC_NAME',
  'APPLE_ID',
  'APPLE_APP_SPECIFIC_PASSWORD',
  'APPLE_TEAM_ID'
]) {
  if (process.env[name] === '') {
    delete process.env[name];
  }
}

const signing = Boolean(process.env['CSC_LINK'] || process.env['CSC_NAME']);

await build({
  publish: 'never',
  config: {
    appId: 'app.cathode.desktop',
    productName: 'Cathode',
    copyright: 'Cathode contributors, GPL-3.0-or-later',
    electronVersion: require('electron/package.json').version,
    directories: { app: stage, output: join(dist, 'release'), buildResources: join(workspace, 'apps/desktop/assets') },
    files: ['**/*', '!**/*.map'],
    // cathode-server serves the editor to other devices on the network (Settings ▸ Audio setup) and cannot read the
    // asar archive: keep the editor beside it as plain files (main.ts points the Server at app.asar.unpacked).
    asarUnpack: ['editor/**'],
    extraResources: [
      { from: engine, to: 'engine', filter: ['**/*', '!licenses{,/**/*}'] },
      { from: licences, to: 'licenses' }
    ],
    afterExtract: ({ appOutDir, electronPlatformName }) => {
      if (electronPlatformName === 'darwin') {
        cpSync(join(appOutDir, 'LICENSE'), join(licences, 'LICENSE.electron.txt'));
        cpSync(join(appOutDir, 'LICENSES.chromium.html'), join(licences, 'LICENSES.chromium.html'));
      }
    },
    npmRebuild: false,
    // The shipped binary is Cathode and nothing else: it cannot be run as a plain Node (ELECTRON_RUN_AS_NODE), takes
    // no NODE_OPTIONS and no --inspect, and loads its code only from app.asar, whose contents it checks (macOS and
    // Windows). Otherwise any local program could run its own code under Cathode's identity and microphone
    // permission. electron-builder flips the fuses before it signs.
    electronFuses: {
      runAsNode: false,
      enableNodeOptionsEnvironmentVariable: false,
      enableNodeCliInspectArguments: false,
      enableEmbeddedAsarIntegrityValidation: true,
      onlyLoadAppFromAsar: true
    },
    icon: join(workspace, 'apps/desktop/assets/icon.png'),
    // Double-clicking a preset opens it in Cathode, which imports it into the library (main.ts, open-files.ts).
    fileAssociations: [
      { ext: 'cthpreset', name: 'Cathode Preset', role: 'Editor', mimeType: 'application/x-cathode-preset' }
    ],
    artifactName: '${productName}-${version}-${os}-${arch}.${ext}',
    mac: {
      category: 'public.app-category.music',
      target: ['dmg', 'zip'],
      // Ad hoc ('-') without a certificate: Apple Silicon will not run unsigned code at all.
      identity: signing ? undefined : '-',
      hardenedRuntime: signing,
      entitlements: join(workspace, 'apps/desktop/assets/entitlements.mac.plist'),
      entitlementsInherit: join(workspace, 'apps/desktop/assets/entitlements.mac.plist'),
      notarize: signing && Boolean(process.env['APPLE_ID']),
      extendInfo: {
        NSMicrophoneUsageDescription: 'Cathode processes the guitar signal from your audio interface.'
      }
    },
    win: { target: ['nsis'] },
    nsis: { oneClick: false, allowToChangeInstallationDirectory: true },
    linux: { target: ['AppImage'], category: 'Audio' }
  }
});
