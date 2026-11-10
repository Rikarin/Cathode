import { chmodSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { LogFile } from './log-file';

const At = new Date('2026-11-10T12:00:00.000Z');
const Stamp = '2026-11-10T12:00:00.000Z';

describe('LogFile', () => {
  let folder: string;
  let file: string;
  let open: LogFile[];
  const log = (maxBytes?: number) => {
    const made = new LogFile(file, maxBytes, () => At);
    open.push(made);
    return made;
  };
  const read = (name = 'host.log') => readFileSync(join(folder, 'logs', name), 'utf8');

  beforeEach(() => {
    folder = mkdtempSync(join(tmpdir(), 'cathode-log-'));
    file = join(folder, 'logs', 'host.log');
    open = [];
  });

  afterEach(() => {
    open.forEach(made => made.close());
    rmSync(folder, { recursive: true, force: true });
  });

  it('makes its folder and has each line on disk, with the time, as soon as it is written', () => {
    const host = log();
    host.write('cathode-host 1.0 serving on /tmp/x.sock');
    expect(read()).toBe(`${Stamp} cathode-host 1.0 serving on /tmp/x.sock\n`);

    host.write('error: the device was lost\r\n');
    expect(read().split('\n')).toEqual([
      `${Stamp} cathode-host 1.0 serving on /tmp/x.sock`,
      `${Stamp} error: the device was lost`,
      ''
    ]);
  });

  it('appends to the file of an earlier run', () => {
    log().write('first run');
    log().write('second run');

    expect(read()).toBe(`${Stamp} first run\n${Stamp} second run\n`);
  });

  it('starts a new file at the size limit and keeps one old one', () => {
    const line = 'x'.repeat(40); // 66 bytes with the time and the line end
    const host = log(200);
    for (let i = 0; i < 3; i++) {
      host.write(line);
    }

    expect(readdirSync(join(folder, 'logs'))).toEqual(['host.log']);
    host.write(`${line} 4`);
    expect(readdirSync(join(folder, 'logs')).sort()).toEqual(['host.log', 'host.log.1']);
    expect(read('host.log.1').split('\n')).toHaveLength(4);
    expect(read()).toBe(`${Stamp} ${line} 4\n`);

    // The next turn replaces the old file: never more than two.
    for (let i = 0; i < 3; i++) {
      host.write(line);
    }

    expect(readdirSync(join(folder, 'logs')).sort()).toEqual(['host.log', 'host.log.1']);
    expect(read('host.log.1')).toContain(`${line} 4`);
  });

  it('counts what an earlier run left towards the limit', () => {
    log(200).write('y'.repeat(150));
    log(200).write('z'.repeat(40));

    expect(read('host.log.1')).toContain('yyy');
    expect(read()).toBe(`${Stamp} ${'z'.repeat(40)}\n`);
  });

  it('writes a line longer than the limit rather than dropping it', () => {
    const host = log(50);
    host.write('w'.repeat(100));

    expect(read()).toContain('w'.repeat(100));
  });

  it.skipIf(process.platform === 'win32' || process.getuid?.() === 0)(
    'never throws when the file cannot be written, and writes again once it can',
    () => {
      writeFileSync(join(folder, 'logs'), 'a file where the folder should be');
      const host = log();
      expect(() => host.write('lost')).not.toThrow();

      rmSync(join(folder, 'logs'));
      host.write('kept');
      expect(read()).toBe(`${Stamp} kept\n`);

      chmodSync(join(folder, 'logs'), 0o700);
    }
  );
});
