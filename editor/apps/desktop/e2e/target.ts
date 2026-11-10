import { existsSync } from 'node:fs';
import { join, resolve } from 'node:path';

const dist = resolve(__dirname, '../../../dist');

/**
 * Whether the tests drive the packaged app (CATHODE_PACKAGED=1, after `pnpm dist`) instead of the build under dist/
 * run by the electron package. The packaged app is what users get: its own Electron, the editor inside the asar
 * archive, and the published engine (a NativeAOT host and a self-contained Server) in resources/engine.
 */
export const packaged = Boolean(process.env['CATHODE_PACKAGED']);

/** How to start the app under test: the executable (none: the electron package's) and its arguments. */
export function launchTarget(): { executablePath?: string; args: string[] } {
  return packaged ? { executablePath: packagedExecutable(), args: [] } : { args: [join(dist, 'apps/desktop/main.js')] };
}

/** The packaged app's executable, where electron-builder leaves the app unpacked beside the installers. */
function packagedExecutable(): string {
  const release = join(dist, 'release');
  const arch = process.arch === 'x64' ? '' : `-${process.arch}`;
  const candidates =
    process.platform === 'darwin'
      ? [`mac${arch}/Cathode.app/Contents/MacOS/Cathode`]
      : process.platform === 'win32'
        ? [`win${arch}-unpacked/Cathode.exe`]
        : [`linux${arch}-unpacked/cathode`, `linux${arch}-unpacked/Cathode`];
  const found = candidates.map(candidate => join(release, candidate)).find(path => existsSync(path));
  if (!found) {
    throw new Error(
      `No packaged app in ${release} (looked for ${candidates.join(', ')}): run pnpm dist first, or unset CATHODE_PACKAGED.`
    );
  }

  return found;
}
