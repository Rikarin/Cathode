import { readFile, stat } from 'node:fs/promises';
import { basename, resolve } from 'node:path';

/** A preset package, as the editor exports one (Library ▸ Export). */
export const PresetExtension = '.cthpreset';
/** The Server's own limit for an imported package (PresetPackage.MaxPackageBytes). */
export const MaxPresetBytes = 512 * 1024 * 1024;

/** A preset file the OS asked the app to open, as the editor gets it: its bytes, or why it could not be read. */
export interface OpenedPreset {
  name: string;
  data: Uint8Array | null;
  error: string | null;
}

export function isPresetFile(path: string): boolean {
  return path.toLowerCase().endsWith(PresetExtension);
}

/**
 * The preset files on a command line: Windows and Linux pass a double-clicked file as an argument (macOS sends
 * `open-file` instead). Switches and everything else (the executable, main.js when run from the source tree, what
 * Chromium appends) are left out; relative paths are taken from `workingDirectory`.
 */
export function presetArguments(argv: readonly string[], workingDirectory: string): string[] {
  return argv
    .slice(1)
    .filter(argument => !argument.startsWith('-') && isPresetFile(argument))
    .map(argument => resolve(workingDirectory, argument));
}

/** Reads a preset file for the editor to import. It never throws: a file that cannot be read comes back as its error. */
export async function readPreset(path: string): Promise<OpenedPreset> {
  const name = basename(path);
  try {
    if (!isPresetFile(path)) {
      throw new Error(`${name} is not a Cathode preset (${PresetExtension}).`);
    }

    const info = await stat(path);
    if (!info.isFile()) {
      throw new Error(`${name} is not a file.`);
    }

    if (info.size > MaxPresetBytes) {
      throw new Error(`${name} is larger than a preset can be (${MaxPresetBytes / (1024 * 1024)} MB).`);
    }

    return { name, data: new Uint8Array(await readFile(path)), error: null };
  } catch (error) {
    const missing = (error as NodeJS.ErrnoException).code === 'ENOENT';
    return { name, data: null, error: missing ? `${name} was not found.` : (error as Error).message };
  }
}

/**
 * Files waiting for the editor. The OS can hand over a file before the window exists, while the engine starts or
 * while the window reloads: they are kept, in order, until the editor says it is ready, and given to it then.
 */
export class FileInbox {
  private readonly waiting: string[] = [];
  private deliver: ((path: string) => void) | null = null;

  add(path: string): void {
    if (this.deliver) {
      this.deliver(path);
    } else if (!this.waiting.includes(path)) {
      this.waiting.push(path);
    }
  }

  /** The editor listens: it gets what waited, and every file from now on. */
  ready(deliver: (path: string) => void): void {
    this.deliver = deliver;
    for (const path of this.waiting.splice(0)) {
      deliver(path);
    }
  }

  /** The editor is gone (the window closed or reloads): files wait again. */
  closed(): void {
    this.deliver = null;
  }
}
