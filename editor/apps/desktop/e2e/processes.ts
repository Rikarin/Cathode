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
  return commandLines(Sidecars).filter(
    line => line.includes('cathode-host') && line.includes('--serve') && line.includes(socket(pid))
  );
}

/** The Servers that app runs (on its host's socket), by command line. */
export function servers(pid: number): string[] {
  return commandLines(Sidecars).filter(line => line.includes('cathode-server') && line.includes(socket(pid)));
}

/** The connection the desktop app hands its editor: the local Server's address and token. */
export function connection(window: Page): Promise<{ baseUrl: string; token: string }> {
  return window.evaluate(() =>
    (
      globalThis as unknown as { cathode: { connection(): Promise<{ baseUrl: string; token: string }> } }
    ).cathode.connection()
  );
}

/** The executables a sidecar can run as on Windows: its own, or `dotnet` with its dll (a development checkout). */
const Sidecars = ['cathode-host.exe', 'cathode-server.exe', 'dotnet.exe'];

/**
 * Every process's command line, as any account on the machine can list them; on Windows, only those of the processes
 * named in `windowsNames` when it is given. Reading every process's command line through WMI took 23 s a call on the
 * Windows ARM64 runner, where the engine-restart test's ten calls ran out its two minutes; the provider filters by
 * name before it reads any.
 */
export function commandLines(windowsNames?: readonly string[]): string[] {
  // A single-quoted PowerShell string (its quotes doubled), so no double quote goes through Node's argument quoting.
  const filter = windowsNames ? ` -Filter '${windowsNames.map(name => `Name=''${name}''`).join(' OR ')}'` : '';
  const output =
    process.platform === 'win32'
      ? execFileSync(
          'powershell.exe',
          [
            '-NoProfile',
            '-Command',
            `Get-CimInstance Win32_Process -Property CommandLine${filter} | ForEach-Object { $_.CommandLine }`
          ],
          { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 }
        )
      : execFileSync('ps', ['-ewwo', 'args'], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  return output.split(/\r?\n/).filter(Boolean);
}
