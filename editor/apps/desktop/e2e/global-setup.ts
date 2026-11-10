import { flipFuses, FuseV1Options, FuseVersion } from '@electron/fuses';
import { cpSync, rmSync } from 'node:fs';
import { basename, join } from 'node:path';
import { drivenFolder, packaged, packagedApp, shippedFolder } from './target';

/**
 * Before a run against the packaged app (CATHODE_PACKAGED=1): copies the unpacked app out of dist/release and lets the
 * copy take --inspect again, which Playwright needs to reach Electron's main process and the shipped binary refuses.
 * Nothing else differs: the same Electron, app.asar, editor and engine, and every other fuse as shipped.
 * packaged.spec.ts checks the fuses of the app in dist/release, the one the installers carry.
 */
export default async function prepare(): Promise<void> {
  if (!packaged) {
    return;
  }

  const shipped = packagedApp(shippedFolder);
  rmSync(drivenFolder, { recursive: true, force: true });
  // macOS frameworks are full of symbolic links, which have to stay links for the bundle to load.
  cpSync(shipped.unpacked, join(drivenFolder, basename(shipped.unpacked)), {
    recursive: true,
    verbatimSymlinks: true
  });
  await flipFuses(packagedApp(drivenFolder).fuses, {
    version: FuseVersion.V1,
    // Changing the binary voids its signature, and Apple Silicon runs no unsigned code: sign the copy ad hoc.
    resetAdHocDarwinSignature: process.platform === 'darwin',
    [FuseV1Options.EnableNodeCliInspectArguments]: true
  });
}
