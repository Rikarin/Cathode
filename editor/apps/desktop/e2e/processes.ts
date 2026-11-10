import type { ElectronApplication, Page } from '@playwright/test';
import { execFileSync } from 'node:child_process';
import { readdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

// The app and its engine sidecars as processes: what other programs on the machine can see of them.

/** The app's main process (which, on Windows, is not the one Playwright started). */
export function mainPid(app: ElectronApplication): Promise<number> {
  return app.evaluate(() => process.pid);
}

/** The socket between the host and Server of the app whose main process is `pid`. */
export function socket(pid: number): string {
  return join(tmpdir(), `cathode-${pid}.sock`);
}

/** Whether that socket is there: listed, as Windows cannot stat a Unix socket. */
export function socketExists(pid: number): boolean {
  return readdirSync(tmpdir()).includes(`cathode-${pid}.sock`);
}

/** The engine hosts that app runs (on its socket), by command line. */
export function hosts(pid: number): string[] {
  return commandLines().filter(
    line => line.includes('cathode-host') && line.includes('--serve') && line.includes(socket(pid))
  );
}

/** The Servers that app runs (on its host's socket), by command line. */
export function servers(pid: number): string[] {
  return commandLines().filter(line => line.includes('cathode-server') && line.includes(socket(pid)));
}

/** The connection the desktop app hands its editor: the local Server's address and token. */
export function connection(window: Page): Promise<{ baseUrl: string; token: string }> {
  return window.evaluate(() =>
    (
      globalThis as unknown as { cathode: { connection(): Promise<{ baseUrl: string; token: string }> } }
    ).cathode.connection()
  );
}

/** Every process's command line, as any account on the machine can list them. */
export function commandLines(): string[] {
  const output =
    process.platform === 'win32'
      ? execFileSync(
          'powershell.exe',
          ['-NoProfile', '-Command', 'Get-CimInstance Win32_Process | ForEach-Object { $_.CommandLine }'],
          { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 }
        )
      : execFileSync('ps', ['-ewwo', 'args'], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  return output.split(/\r?\n/).filter(Boolean);
}
