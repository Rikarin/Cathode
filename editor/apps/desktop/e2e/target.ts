import { existsSync } from 'node:fs';
import { join, resolve } from 'node:path';

const dist = resolve(__dirname, '../../../dist');

/**
 * Whether the tests drive the packaged app (CATHODE_PACKAGED=1, after `pnpm dist`) instead of the build under dist/
 * run by the electron package. The packaged app is what users get: its own Electron, the editor inside the asar
 * archive, and the published engine (a NativeAOT host and a self-contained Server) in resources/engine.
 */
export const packaged = Boolean(process.env['CATHODE_PACKAGED']);

/** Where electron-builder leaves the app unpacked beside the installers: the app as shipped. */
export const shippedFolder = join(dist, 'release');

/**
 * Where the copy of the unpacked app that the tests start is made (global-setup.ts). Playwright drives Electron's main
 * process through Node's inspector, and the shipped binary refuses --inspect (its fuses, scripts/package.mjs), so the
 * copy has that one fuse switched back on.
 */
export const drivenFolder = join(dist, 'apps/desktop-e2e');

/** How to start the app under test: the executable (none: the electron package's) and its arguments. */
export function launchTarget(): { executablePath?: string; args: string[] } {
  return packaged
    ? { executablePath: packagedApp(drivenFolder).executable, args: [] }
    : { args: [join(dist, 'apps/desktop/main.js')] };
}

/**
 * The unpacked app in `folder` (the shipped one or the tests' copy): the folder electron-builder names after the OS
 * and CPU, the executable in it, and what Electron's fuses are read from and written to (the bundle on macOS).
 */
export function packagedApp(folder: string): { unpacked: string; executable: string; fuses: string } {
  const arch = process.arch === 'x64' ? '' : `-${process.arch}`;
  const [unpacked, executables, bundle] =
    process.platform === 'darwin'
      ? [`mac${arch}`, ['Cathode.app/Contents/MacOS/Cathode'], 'Cathode.app']
      : process.platform === 'win32'
        ? [`win${arch}-unpacked`, ['Cathode.exe'], null]
        : [`linux${arch}-unpacked`, ['cathode', 'Cathode'], null];
  const executable = executables.map(name => join(folder, unpacked, name)).find(path => existsSync(path));
  if (!executable) {
    throw new Error(
      `No packaged app in ${folder} (looked for ${executables.map(name => `${unpacked}/${name}`).join(', ')}): ` +
        'run pnpm dist first, or unset CATHODE_PACKAGED.'
    );
  }

  return { unpacked: join(folder, unpacked), executable, fuses: bundle ? join(folder, unpacked, bundle) : executable };
}
