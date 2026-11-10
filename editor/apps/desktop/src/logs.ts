import { app, shell } from 'electron';
import { existsSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { LogFile } from './log-file';

// The desktop app's log files, in the system's folder for them (~/Library/Logs/Cathode on macOS, logs/ under the
// app's data folder elsewhere): main.log for the app itself, host.log and server.log for what the two engine
// processes print. A packaged GUI app has no console, so this is where a crash or a failed start leaves its reason,
// and what a bug report can attach (Help ▸ Show Logs).

type LogName = 'main' | 'host' | 'server';

const files = new Map<LogName, LogFile>();

function folder(): string {
  return app.getPath('logs');
}

function file(name: LogName): LogFile | null {
  let log = files.get(name);
  if (!log) {
    try {
      log = new LogFile(join(folder(), `${name}.log`));
    } catch {
      return null; // no logs folder to be had: the console is all there is
    }

    files.set(name, log);
  }

  return log;
}

function text(error: unknown): string {
  return error instanceof Error ? (error.stack ?? error.message) : String(error);
}

/** The app's own messages, to main.log; warnings and errors also go to the console, for a development run. */
export const log = {
  info(message: string): void {
    file('main')?.write(message);
  },
  warn(message: string): void {
    console.warn(`Cathode: ${message}`);
    file('main')?.write(`warning: ${message}`);
  },
  error(message: string, error?: unknown): void {
    console.error(`Cathode: ${message}`, ...(error === undefined ? [] : [error]));
    file('main')?.write(`error: ${message}${error === undefined ? '' : `: ${text(error)}`}`);
  }
};

/** A line an engine process printed, to host.log or server.log. */
export function logSidecar(name: 'host' | 'server', line: string): void {
  file(name)?.write(line);
}

/** Shows the log files in the system's file manager. */
export function showLogs(): void {
  const main = join(folder(), 'main.log');
  if (existsSync(main)) {
    shell.showItemInFolder(main);
    return;
  }

  mkdirSync(folder(), { recursive: true });
  void shell.openPath(folder());
}
