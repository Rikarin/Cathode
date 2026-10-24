// Runs the built desktop app for development (`nx serve desktop`). On macOS the menu bar, Dock and app switcher take
// the app's name from the bundle, not from app.setName, so a bare `electron` shows "Electron". This runs a copy of
// Electron.app named Cathode instead: cloned once per Electron version into dist/apps/desktop-dev, its Info.plist
// renamed, and re-signed ad hoc (Apple Silicon refuses a bundle whose plist no longer matches its signature). The
// executable keeps its name: Electron takes any other name for a packaged app (app.isPackaged), which would look for
// the editor and engine inside the bundle.
import { execFileSync, spawn } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join, resolve } from 'node:path';

const workspace = resolve(import.meta.dirname, '../../..');
const main = join(workspace, 'dist/apps/desktop/main.js');
const require = createRequire(import.meta.url);
const electron = require('electron'); // the path of the stock Electron binary

const executable = process.platform === 'darwin' ? renamedBundle() : electron;
const child = spawn(executable, [main, ...process.argv.slice(2)], { stdio: 'inherit' });
child.on('exit', (code, signal) => process.exit(signal ? 1 : (code ?? 0)));
for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, () => child.kill(signal));
}

/** Clones Electron.app to Cathode.app when the Electron version changed, and returns its binary. */
function renamedBundle() {
  const stock = resolve(dirname(electron), '../..'); // .../Electron.app/Contents/MacOS/Electron
  const out = join(workspace, 'dist/apps/desktop-dev');
  const bundle = join(out, 'Cathode.app');
  const stamp = join(out, 'electron-version');
  const version = readFileSync(join(dirname(require.resolve('electron')), 'dist/version'), 'utf8').trim();
  if (!existsSync(bundle) || !existsSync(stamp) || readFileSync(stamp, 'utf8') !== version) {
    rmSync(out, { recursive: true, force: true });
    mkdirSync(out, { recursive: true });
    try {
      execFileSync('cp', ['-cR', stock, bundle]); // an APFS clone: instant and takes no space
    } catch {
      execFileSync('ditto', [stock, bundle]);
    }
    const plist = join(bundle, 'Contents/Info.plist');
    for (const [key, value] of [
      ['CFBundleName', 'Cathode'],
      ['CFBundleDisplayName', 'Cathode'],
      ['CFBundleIdentifier', 'app.cathode.desktop.dev']
    ]) {
      execFileSync('plutil', ['-replace', key, '-string', value, plist]);
    }
    execFileSync('codesign', ['--force', '--deep', '--sign', '-', bundle], { stdio: 'ignore' });
    writeFileSync(stamp, version);
  }
  return join(bundle, 'Contents/MacOS/Electron');
}
