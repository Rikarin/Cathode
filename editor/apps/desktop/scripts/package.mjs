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
const require = createRequire(import.meta.url);

for (const [path, hint] of [
  [join(dist, 'apps/desktop/main.js'), 'pnpm exec nx build desktop'],
  [join(dist, 'apps/editor/browser/index.html'), 'pnpm exec nx build editor'],
  [engine, 'node apps/desktop/scripts/publish-engine.mjs']
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
    extraResources: [{ from: engine, to: 'engine' }],
    npmRebuild: false,
    icon: join(workspace, 'apps/desktop/assets/icon.png'),
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
