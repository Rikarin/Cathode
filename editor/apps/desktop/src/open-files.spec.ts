import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { FileInbox, presetArguments, readPreset } from './open-files';

describe('presetArguments', () => {
  const cwd = resolve('/somewhere');

  it('finds the preset a packaged app was started with', () => {
    expect(presetArguments(['/opt/Cathode/cathode', '/home/me/Clean.cthpreset'], cwd)).toEqual([
      resolve('/home/me/Clean.cthpreset')
    ]);
  });

  it('skips the executable, main.js, switches and other files', () => {
    const argv = ['electron', 'dist/apps/desktop/main.js', '--inspect=9229', '--file=x.cthpreset', 'notes.txt'];
    expect(presetArguments(argv, cwd)).toEqual([]);
  });

  it('takes relative paths from the working directory, whatever the case of the extension', () => {
    expect(presetArguments(['cathode', 'presets/Lead.CTHPRESET', '--allow-file-access-from-files'], cwd)).toEqual([
      resolve(cwd, 'presets/Lead.CTHPRESET')
    ]);
  });

  it('never takes the executable itself for a preset', () => {
    expect(presetArguments(['odd.cthpreset'], cwd)).toEqual([]);
  });
});

describe('readPreset', () => {
  let folder: string;

  beforeEach(() => {
    folder = mkdtempSync(join(tmpdir(), 'cathode-open-'));
  });
  afterEach(() => rmSync(folder, { recursive: true, force: true }));

  it('reads the file with its name', async () => {
    const path = join(folder, 'Clean.cthpreset');
    writeFileSync(path, Buffer.from([80, 75, 3, 4]));
    expect(await readPreset(path)).toEqual({
      name: 'Clean.cthpreset',
      data: new Uint8Array([80, 75, 3, 4]),
      error: null
    });
  });

  it('reports a missing file instead of throwing', async () => {
    expect(await readPreset(join(folder, 'Gone.cthpreset'))).toEqual({
      name: 'Gone.cthpreset',
      data: null,
      error: 'Gone.cthpreset was not found.'
    });
  });

  it('refuses a folder and a file of another kind', async () => {
    mkdirSync(join(folder, 'Folder.cthpreset'));
    writeFileSync(join(folder, 'settings.json'), '{}');
    expect((await readPreset(join(folder, 'Folder.cthpreset'))).error).toBe('Folder.cthpreset is not a file.');
    expect((await readPreset(join(folder, 'settings.json'))).error).toBe(
      'settings.json is not a Cathode preset (.cthpreset).'
    );
  });
});

describe('FileInbox', () => {
  it('keeps files until the editor is ready, then hands them over in order', () => {
    const inbox = new FileInbox();
    const got: string[] = [];
    inbox.add('a');
    inbox.add('b');
    inbox.add('a'); // the OS can repeat a file while the app starts
    inbox.ready(path => got.push(path));
    expect(got).toEqual(['a', 'b']);

    inbox.add('c');
    expect(got).toEqual(['a', 'b', 'c']);
  });

  it('keeps files again while the window reloads, and gives each only once', () => {
    const inbox = new FileInbox();
    const first: string[] = [];
    const second: string[] = [];
    inbox.ready(path => first.push(path));
    inbox.closed();
    inbox.add('a');
    inbox.ready(path => second.push(path));
    inbox.closed();
    inbox.ready(path => second.push(path));
    expect(first).toEqual([]);
    expect(second).toEqual(['a']);
  });
});
