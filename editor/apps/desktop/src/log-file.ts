import { closeSync, fstatSync, mkdirSync, openSync, renameSync, writeSync } from 'node:fs';
import { dirname } from 'node:path';

/** A few MB of text is weeks of an engine's output, and small enough to attach to a bug report. */
const DefaultMaxBytes = 2 * 1024 * 1024;

/**
 * A log file that stays small: lines are appended with the time in front, and once the file would pass `maxBytes` it
 * becomes `<file>.1` (replacing the one before) and a new file starts, so two files at most are kept.
 *
 * Each line is written to the file before `write` returns, so what a process said last is on disk when the app dies
 * with it. Writing never throws: a log that cannot be written (a full disk, a read-only folder) must not take the app
 * down, and is tried again at the next line.
 */
export class LogFile {
  private fd: number | null = null;
  private size = 0;

  constructor(
    readonly file: string,
    private readonly maxBytes = DefaultMaxBytes,
    private readonly now: () => Date = () => new Date()
  ) {}

  write(line: string): void {
    const text = `${this.now().toISOString()} ${line.replace(/\r?\n$/, '')}\n`;
    const bytes = Buffer.byteLength(text);
    try {
      let fd = this.fd ?? this.open();
      if (this.size > 0 && this.size + bytes > this.maxBytes) {
        closeSync(fd);
        this.fd = null;
        renameSync(this.file, `${this.file}.1`);
        fd = this.open();
      }

      writeSync(fd, text);
      this.size += bytes;
    } catch {
      this.close();
    }
  }

  close(): void {
    if (this.fd !== null) {
      try {
        closeSync(this.fd);
      } catch {
        // Already gone.
      }

      this.fd = null;
    }
  }

  private open(): number {
    mkdirSync(dirname(this.file), { recursive: true });
    const fd = openSync(this.file, 'a');
    this.fd = fd;
    this.size = fstatSync(fd).size;
    return fd;
  }
}
